using System.Collections.Concurrent;
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
    private bool _connected;
    private bool _isconnecting = false;
    
    private readonly ILogger _logger;
    private readonly Uri _relayUri;
    private string _appVer = "";

    private Thread _pollThread;
    private CancellationTokenSource _pollCts;

    private Queue<Response> _queuedMessages;
    private Dictionary<string, IRelayMessageNotify> _listeners;
    private ManualResetEventSlim _resetEvent;
    
    public bool Connected => _connected;
    public string Version => _appVer;
    
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
        _listeners = new Dictionary<string, IRelayMessageNotify>();
        _resetEvent = new ManualResetEventSlim();
        _queuedMessages = new Queue<Response>();
        _pollCts = new CancellationTokenSource();
        _pollThread = new Thread(PollThread);
        _relayUri = uri;
        _logger = logger;
    }

    public void AddListener(string messageId, IRelayMessageNotify notify)
    {
        _listeners[messageId] = notify;
    }

    private void PollThread()
    {
        while (true)
        {
            if (_pollCts.IsCancellationRequested)
            {
                return;
            }

            if (_socket.Available > 0)
            {
                var buffer = new byte[_socket.Available];
                _socket.Receive(buffer);
                var response = new Response(BencodeSerializer.Deserialize<BDict>(buffer));

                bool asyncReceived = false;
                if (_listeners.ContainsKey(response.RequestName))
                {
                    bool genericInvoke = true;
                    
                    var listener = _listeners[response.RequestName];
                    var methods = listener.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
                    foreach (var method in methods)
                    {
                        var attr = method.GetCustomAttribute<RelayMessageHandlerAttribute>();
                        if (attr is not null && attr.MessageName.Equals(response.RequestName))
                        {
                            var inputType = method.GetParameters()[0].ParameterType;
                            var content = response.ReadContent(inputType);
                            if (content is not null)
                            {
                                method.Invoke(listener, new[] { content });
                                genericInvoke = false;
                                break;
                            }
                        }
                    }

                    if (genericInvoke)
                    {
                        _listeners[response.RequestName].ReceiveRelayMessage(response);
                    }

                    asyncReceived = true;
                }

                if (!asyncReceived)
                {
                    _queuedMessages.Enqueue(response);

                    if (_queuedMessages.Count == 1)
                    {
                        _resetEvent.Set();
                    }
                }
            }
        }
    }

    public async Task ConnectToRelayAsync(CancellationToken token)
    {
        if (_connected) throw new InvalidOperationException();
        
        IPAddress[] hostIp;
        do
        {
            try
            {
                hostIp = await Dns.GetHostAddressesAsync(_relayUri.Host, AddressFamily.InterNetwork, token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                if (hostIp.Length == 0)
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);
                    continue;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            break;

        } while (true);

        _relayEp = new IPEndPoint(hostIp[0], _relayUri.Port);
        
        _logger.LogTrace("Begin relay connection {0}", _relayEp);
        int currentDelay = 500;

        var buffer = new byte[128];

        while (true)
        {
            _isconnecting = true;
            try
            {
                token.ThrowIfCancellationRequested();
                bool shouldDelay = false;
                
                
                if (!_socket.Connected)
                {
                    _socket = NewSocket();
                    
                    try
                    {
                        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
                        cancel.CancelAfter(250);
                        await _socket.ConnectAsync(_relayEp, cancel.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        shouldDelay = true;
                    }
                }

                string actualVersion = "";
                string clientVersion = "";
                if (!shouldDelay)
                {
                    var welcomeRequest = new NetMessageWelcome
                    {
                        Version = "1.0.0"
                    };
                    clientVersion = welcomeRequest.Version;
                    try
                    {
                        await SendAsync(welcomeRequest, token).ConfigureAwait(false);

                        using var cancelToken = CancellationTokenSource.CreateLinkedTokenSource(token);
                        cancelToken.CancelAfter(1000);

                        var response = await ReceiveAsync().ConfigureAwait(false);
                        if (response.IsError)
                        {
                            var error = response.ReadContent<ResponseError>();
                            
                            _logger.LogError("Error during connection to relay: {0}", error.Message);
                            
                            await _socket.DisconnectAsync(true, token).ConfigureAwait(false);
                            return;
                        }
                        else
                        {
                            var content = response.ReadContent<NetMessageWelcome>();
                            actualVersion = content.Version;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }
                        else
                        {
                            shouldDelay = true;
                        }
                    }
                    catch (Exception)
                    {
                        shouldDelay = true;
                    }
                }

                if (shouldDelay)
                {
                    
                    currentDelay = Math.Min(currentDelay * 2, 5000) + Random.Shared.Next(100, 500);
                    _logger.LogWarn("Retrying connection to relay in {0} s.", currentDelay / 1000.0);

                    await Task.Delay(currentDelay, token).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogInfo("Actual version is {0}", actualVersion);

                    if (!actualVersion.Equals(clientVersion))
                    {
                        _logger.LogError("An outdated version detected. Exiting");
                        await _socket.DisconnectAsync(true, token).ConfigureAwait(false);
                    }
                    else
                    {
                        _logger.LogTrace("Connected to relay at {0}", _socket.RemoteEndPoint);
                        _appVer = actualVersion;
                        _connected = true;
                    }

                    _isconnecting = false;
                    _pollThread.Start();
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

    }

    public Task SendAsync<T>(T message, CancellationToken token = default) where T : new() 
        => SendAsync(Request.FromMessage(message), token);
    
    public async Task SendAsync(Request packet, CancellationToken token = default)
    {
        await _socket.SendAsync(packet.GetBytes(), SocketFlags.None, token);
    }

    public Response Receive()
    {
        if (_isconnecting)
        {
            var buffer = new byte[512];
            int recv = _socket.Receive(buffer);

            return new Response(BencodeSerializer.Deserialize<BDict>(buffer.AsSpan(0, recv)));
        }
        else
        {
            if (_queuedMessages.Count == 0)
            {
                _resetEvent.Wait();
                _resetEvent.Reset();
            }

            return _queuedMessages.Dequeue();
        }
    }

    public async Task<Response> ReceiveAsync()
    {
        if (_isconnecting)
        {
            var buffer = new byte[512];
            int recv = await _socket.ReceiveAsync(buffer, SocketFlags.None).ConfigureAwait(false);

            return new Response(BencodeSerializer.Deserialize<BDict>(buffer.AsSpan(0, recv)));
        }
        else
        {
            if (_queuedMessages.Count == 0)
            {
                await Task.Run(() =>
                {
                    _resetEvent.Wait();
                    _resetEvent.Reset();

                }).ConfigureAwait(false);
            }

            return _queuedMessages.Dequeue();
        }
    }
    
    public void Dispose()
    {
        if (_connected)
        {
            _pollCts.Cancel();
            _pollThread.Join();
            _socket.Disconnect(true);
        }
    }
}