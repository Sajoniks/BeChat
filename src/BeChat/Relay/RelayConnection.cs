using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using BeChat.Bencode.Data;
using BeChat.Bencode.Serializer;
using BeChat.Common.Protocol;
using BeChat.Common.Protocol.V1;
using BeChat.Logging;

namespace BeChat.Relay;

public class RelayConnection : IDisposable
{
    private Socket _socket;
    private IPEndPoint? _relayEp;
    private bool _connected = false;
    private bool _pollThreadRunning = false;
    
    private readonly ILogger _logger;
    private readonly Uri _relayUri;
    private string _appVer = "";
    private int _seq = 0;
    private bool _ack = true;

    private Thread? _pollThread;
    private CancellationTokenSource? _pollCts;

    private ManualResetEventSlim _socketReceiveEvent = new();
    private ConcurrentQueue<Response> _queuedMessages = new();
    private ConcurrentQueue<Request> _queuedRequests = new();
    private Dictionary<string, IRelayMessageNotify> _listeners = new();

    public event EventHandler<Task<bool>>? OnReconnect;
    public event EventHandler? OnReconnected;
    public event EventHandler? OnDisconnect;

    public bool Connected => _connected;
    public string Version => _appVer;
    public IPEndPoint RelayEndPoint => _relayEp;
    public IPEndPoint LocalEndPoint => _socket.LocalEndPoint as IPEndPoint;
    
    private static Socket NewSocket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.LingerState = new LingerOption(true, 0);
        return socket;
    }
    
    public RelayConnection(Uri uri, ILogger logger)
    {
        _socket = NewSocket();
        _relayUri = uri;
        _logger = logger;
    }

    public void AddListener(string messageId, IRelayMessageNotify notify)
    {
        _listeners[messageId] = notify;
    }

    public void AddListener<T>(IRelayMessageNotify notify) where T : new()
    {
        _listeners[NetMessage<T>.GetMessageId()] = notify;
    }

    private void StopPollThread()
    {
        if (!_pollThreadRunning)
        {
            throw new InvalidOperationException();
        }

        _pollThreadRunning = false;
        _pollCts!.Cancel();
        _pollThread!.Join();
    }
    
    private void StartPollThread()
    {
        if (_pollThreadRunning)
        {
            throw new InvalidOperationException();
        }

        _pollThreadRunning = true;
        _pollCts = new CancellationTokenSource();
        _pollThread = new Thread(PollThread)
        {
            Name = "Relay Connection Polling Thread"
        };
        _pollThread.Start();
    }
    
    private void PollThread()
    {
        var token = _pollCts!.Token;
        _pollThreadRunning = true;

        var buffer = new byte[16 * 1024];

        while (true)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (!_socket.Poll(0, SelectMode.SelectRead))
            {
                continue;
            }

            if (_socket.Available == 0)
            {
                try
                {
                    _socket.Disconnect(true);
                }
                catch (Exception)
                {
                    // ignore
                }

                _pollThreadRunning = false;
                _pollCts.Cancel();
                _pollCts.Dispose();
                _pollCts = null;
                _pollThread = null;
                _connected = false;
                
                _logger.LogError("Connection to relay was lost");

                OnDisconnect?.Invoke(this, EventArgs.Empty);
                var t = ConnectToRelayAsync(CancellationToken.None);
                OnReconnect?.Invoke(this, t);

                _ = t.ContinueWith(b =>
                {
                    if (b.Result)
                    {
                        OnReconnected?.Invoke(this, EventArgs.Empty);
                    }
                }, CancellationToken.None);
                
                return;
            }
            
            int recv = 0;
            try
            {
                recv = _socket.ReceiveAsync(buffer, SocketFlags.None, _pollCts.Token).GetAwaiter().GetResult();
                Debug.WriteLine("Received {0} bytes from relay", recv);
            }
            catch (OperationCanceledException e)
            {
                if (_pollCts.Token.IsCancellationRequested)
                {
                    // thread was requested to stop
                    return;
                }
            }
            catch (Exception)
            {
                // failed to receive message
                continue;
            }

            bool InvokeHandler(Response response)
            {
                bool InvokeGenericHandler(IRelayMessageNotify n, Response r)
                {
                    try
                    {
                        n.ReceiveRelayMessage(r);
                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }

                if (_listeners.TryGetValue(response.RequestName, out var listener))
                {
                    // First try to find matching method
                    var methods = listener.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
                    foreach (var method in methods)
                    {
                        var attr = method.GetCustomAttribute<RelayMessageHandlerAttribute>();
                        if (attr is not null && attr.MessageName.Equals(response.RequestName))
                        {
                            var parms = method.GetParameters();
                            if (parms.Length == 0)
                            {
                                continue;
                            }

                            var inputType = parms[0].ParameterType;
                            try
                            {
                                var content = response.ReadContent(inputType);
                                if (content is not null)
                                {
                                    method.Invoke(listener, new[] { content });
                                    return true;
                                }
                            }
                            catch (NetMessageParseException)
                            {
                                return InvokeGenericHandler(listener, response);
                            }
                        }
                    }
                }

                return false;
            }


            using var stream = new MemoryStream(buffer, 0, recv);
            using var reader = new BinaryReader(stream);

            while (stream.Position < stream.Length)
            {
                long prev = stream.Position;
                Response response;
                try
                {
                    response = new Response(BencodeSerializer.Deserialize<BDict>(reader));
                }
                catch (Exception)
                {
                    // invalid response
                    continue;
                }

                long size = stream.Position - prev;

                Debug.WriteLine("Received response [name = {0}  len = {1}]", response.RequestName, size);

                if (response.HasSequence && response.Sequence == _seq)
                {
                    _ack = true;
                    Interlocked.Increment(ref _seq);

                    if (_queuedRequests.TryDequeue(out var r))
                    {
                        SendAsync(r, CancellationToken.None).GetAwaiter().GetResult();
                    }
                }

                if (!InvokeHandler(response))
                {
                    _queuedMessages.Enqueue(response);
                    _socketReceiveEvent.Set();
                }
            }
        }
    }

    /// <exception cref="InvalidOperationException"></exception>
    public async Task<bool> ConnectToRelayAsync(CancellationToken token)
    {
        if (_connected) throw new InvalidOperationException();

        IPAddress[] hostIp = Array.Empty<IPAddress>();
        while (hostIp.Length == 0)
        {
            try
            {
                hostIp = await Dns.GetHostAddressesAsync(_relayUri.Host, AddressFamily.InterNetwork, token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            try
            {
                if (hostIp.Length == 0)
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        _relayEp = new IPEndPoint(hostIp[0], _relayUri.Port);

        _logger.LogTrace("Begin relay connection {0}", _relayEp);
        int currentDelay = 500;
        var buffer = new byte[512];
        
        while (true)
        {
            if (token.IsCancellationRequested)
            {
                return false;
            }

            if (!_socket.Connected)
            {
                try
                {
                    await _socket.DisconnectAsync(true, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // ignore
                }
                _socket = NewSocket();

                try
                {
                    using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
                    cancel.CancelAfter(1000);
                    await _socket.ConnectAsync(_relayEp, cancel.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    continue;
                }
            }

            string clientVersion = "1.0.0";

            var welcomeRequest = new NetMessageWelcome
            {
                Version = clientVersion
            };

            try
            {
                await SendAsync(welcomeRequest, token).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                _socket.Dispose();
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            Response response;
            try
            {
                using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
                cancel.CancelAfter(1000);
                response = await ReceiveAsync(_socket, buffer, cancel.Token);
                
                if (response.IsError || !response.RequestName.Equals(NetMessage<NetMessageWelcome>.GetMessageId()))
                {
                    throw new InvalidDataException();
                }
            }
            catch (Exception)
            {
                currentDelay = Math.Min(5000, currentDelay + 500);
                await Task.Delay(currentDelay, CancellationToken.None).ConfigureAwait(false);
                continue;
            }
            
            var welcomed = response.ReadContent<NetMessageWelcome>();
            if (welcomed.Version.Equals(clientVersion))
            {
                _logger.LogInfo("Welcomed by relay, version {0}", clientVersion);
                _appVer = welcomed.Version;
                _connected = true;
                
                StartPollThread();

                return true;
            }
            else
            {
                _logger.LogError("Failed to connect to relay because of invalid version (client: {0}, relay: {1})", clientVersion, welcomed.Version);

                try
                {
                    await _socket.DisconnectAsync(true, CancellationToken.None);
                }
                catch (Exception)
                {
                    //
                }

                return false;
            }
        }
    }

    public Task SendAsync<T>(T message, CancellationToken token = default) where T : new() 
        => SendAsync(Request.FromMessage(message), token);
    
    public async Task SendAsync(Request packet, CancellationToken token = default)
    {
        if (!_ack)
        {
            _queuedRequests.Enqueue(packet);
        }
        else
        {
            packet.Sequence = (uint) _seq;
            await _socket.SendAsync(packet.GetBytes(), SocketFlags.None, token);
            _ack = false;
        }
    }

    private async Task<Response> ReceiveAsync(Socket socket, byte[] buffer, CancellationToken token)
    {
        try
        {
            while (true)
            {
                int recv = await _socket.ReceiveAsync(buffer, SocketFlags.None, token).ConfigureAwait(false);
                _ack = true;

                var prev = _seq;
                ++_seq;
                
                var r = new Response(BencodeSerializer.Deserialize<BDict>(buffer.AsSpan(0, recv)));
                if (!r.HasSequence || r.Sequence == prev)
                {
                    return r;
                }
            }
        }
        catch (OperationCanceledException e)
        {
            if (token.IsCancellationRequested)
            {
                _ack = true;
                Interlocked.Increment(ref _seq);
                
                return Response.CreateGenericResponse("error", new ResponseError
                {
                    Message = "Service is unavailable"
                });
            }
            else
            {
                // thread was requested to stop
                throw;
            }
        }
        catch (Exception)
        {
            // failed to receive message
            throw new NetMessageParseException();
        }
    }
    
    public Response Receive()
    {
        if (!_pollThreadRunning) throw new InvalidOperationException("Socket is not connected");
        
        Response? r;
        while (!_queuedMessages.TryDequeue(out r))
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_pollCts!.Token);
            cts.CancelAfter(1000);
            try
            {
                _socketReceiveEvent.Wait(cts.Token);
                _socketReceiveEvent.Reset();
            }
            catch (OperationCanceledException e)
            {
                if (_pollCts.IsCancellationRequested)
                {
                    _ack = true;
                    ++_seq;
                    return Response.CreateGenericResponse("error", new ResponseError
                    {
                        Message = "Request time out"
                    });
                }

                throw;
            }
        }

        return r;
    }

    private static Task WaitOneAsync(WaitHandle waitHandle, CancellationToken token)
    {
        var tcs = new TaskCompletionSource();
        CancellationTokenRegistration reg = token.Register(() =>
        {
            tcs.TrySetCanceled();
        });
        RegisteredWaitHandle handle = ThreadPool.RegisterWaitForSingleObject(waitHandle, (_, _) =>
        {
            tcs.TrySetResult();
        }, null, -1, true);
        _ = tcs.Task.ContinueWith(_ =>
        {
            handle.Unregister(null);
            reg.Unregister();
        }, CancellationToken.None);

        return tcs.Task;
    }
    
    public async Task<Response> ReceiveAsync(CancellationToken token = default)
    {
        if (!_pollThreadRunning) throw new InvalidOperationException("Socket is not connected");

        Response? r;
        while (!_queuedMessages.TryDequeue(out r))
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token, _pollCts!.Token);
            cts.CancelAfter(1000);
            try
            {
                await WaitOneAsync(_socketReceiveEvent.WaitHandle, cts.Token).ConfigureAwait(false);
                _socketReceiveEvent.Reset();
            }
            catch (OperationCanceledException e)
            {
                if (cts.IsCancellationRequested)
                {
                    _ack = true;
                    Interlocked.Increment(ref _seq);

                    return Response.CreateGenericResponse("error", new ResponseError
                    {
                        Message = "Request time out"
                    });
                }
                else if (_pollCts.IsCancellationRequested)
                {
                    _ack = true;
                    Interlocked.Increment(ref _seq);

                    return Response.CreateGenericResponse("error", new ResponseError
                    {
                        Message = "Service is unavailable"
                    });
                }

                throw;
            }
        }

        return r;
    }
    
    public void Dispose()
    {
        if (_connected)
        {
            _pollCts?.Cancel();
            _pollThread?.Join();
            _socket.Disconnect(true);
        }
    }
}