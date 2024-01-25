using System.Net;
using System.Net.Sockets;
using BeChat.Common.Protocol;
using BeChat.Common.Protocol.V1.Messages;
using BeChat.Logging;

namespace BeChat.Relay;

public class RelayConnection : IDisposable
{
    private Socket _socket;
    private IPEndPoint? _relayEp;
    private bool _connected;
    
    private readonly ILogger _logger;
    private readonly Uri _relayUri;

    public bool Connected => _connected;
    
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
    
    public async Task ConnectToRelayAsync(CancellationToken token)
    {
        if (_connected) throw new InvalidOperationException();
        
        IPAddress[] hostIp = Array.Empty<IPAddress>();
        try
        {
            await Dns.GetHostAddressesAsync(_relayUri.Host, AddressFamily.InterNetwork, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _relayEp = new IPEndPoint(hostIp[0], _relayUri.Port);
        
        _logger.LogTrace("Begin relay connection {0}", _relayEp);
        int currentDelay = 500;

        var buffer = new byte[128];

        while (true)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                bool shouldDelay = false;
                if (!_socket.Connected)
                {
                    await _socket.DisconnectAsync(true, token).ConfigureAwait(false);
                    _socket = NewSocket();
                    
                    try
                    {
                        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
                        cancel.CancelAfter(100);
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
                    var welcomeRequest = new WelcomeRequest();
                    clientVersion = welcomeRequest.ClientVersion;
                    _socket.Send(welcomeRequest.GetBytes());

                    int recv = _socket.Receive(buffer);
                    var response = BeChatPacketSerializer.FromBytes<WelcomeResponse>(buffer.AsSpan(0, recv));

                    if (response.IsError)
                    {
                        _logger.LogError("Error during connection to relay: {0}", response.Error!.Message);
                        await _socket.DisconnectAsync(true, token).ConfigureAwait(false);
                        return;
                    }
                    else
                    {
                        actualVersion = response.ActualVersion;
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
                        _connected = true;
                    }

                    return;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        if (_connected)
        {
            _socket.Disconnect(true);
        }
    }
}