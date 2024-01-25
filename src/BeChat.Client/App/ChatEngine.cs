using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using BeChat.Common.Entity;
using BeChat.Common.Protocol.V1.Messages;
using BeChat.Network;

namespace BeChat.Client.App;

public class Message
{
    public string Content { get; }
    public string Peer { get; }

    public Message(string peer, string content)
    {
        Peer = peer;
        Content = content;
    }
}

public class ChatEngine : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly List<Message> _messageHistory;
    private readonly ConcurrentQueue<Message> _pending;
    private bool _disposed = false;

    private readonly NetConnection _connection;
    private readonly EndPoint _remoteEp;
    private readonly Thread _inputThread;
    private readonly Thread _recvThread;
    private readonly char[] _inputBuffer;
    private readonly IRoomPeer _local;
    private int _inputBufferLen = 0;
    
    public event EventHandler<Message>? MessageReceived;

    public IReadOnlyList<Message> Messages => _messageHistory;

    public ReadOnlySpan<char> Input =>
        _inputBufferLen == 0 ? ReadOnlySpan<char>.Empty : _inputBuffer.AsSpan(0, _inputBufferLen);

    public ChatEngine(NetConnection connection, IRoomPeer local)
    {
        _connection = connection;
        _local = local;
        _cts = new CancellationTokenSource();
        _pending = new ConcurrentQueue<Message>();
        _messageHistory = new List<Message>();
        _inputBuffer = new char[2000];
        
        _inputThread = new Thread(() =>
        {
            do
            {
                try
                {
                    _cts.Token.ThrowIfCancellationRequested();

                    if (Console.KeyAvailable)
                    {
                        _cts.Token.ThrowIfCancellationRequested();
                        
                        var key = Console.ReadKey(true);
                        switch (key.Key)
                        {
                            case ConsoleKey.Backspace:
                                if (_inputBufferLen > 0)
                                {
                                    _inputBuffer[_inputBufferLen - 1] = char.MinValue;
                                    --_inputBufferLen;
                                }

                                break;
                            
                            case ConsoleKey.Enter:
                                if (_inputBufferLen > 0)
                                {
                                    var content = "<" + _local.UserName + ">" + new string(Input);
                                    var message = new Message(_local.UserName, new string(Input));
                                    _pending.Enqueue(message);

                                    var bytes = Encoding.UTF8.GetBytes(content);
                                    try
                                    {
                                        _connection.Send(bytes);
                                    }
                                    catch (SocketException)
                                    {
                                        Dispose();
                                    }
                                    catch (IOException)
                                    {
                                        Dispose();
                                    }
                                    finally
                                    {
                                        _inputBufferLen = 0;
                                        Array.Fill(_inputBuffer, char.MinValue);
                                    }
                                }

                                break;
                            
                            default:
                                if (!Char.IsControl(key.KeyChar))
                                {
                                    if (_inputBufferLen < _inputBuffer.Length)
                                    {
                                        _inputBuffer[_inputBufferLen] = key.KeyChar;
                                        _inputBufferLen++;
                                    }
                                }
                                break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }

            } while (true);
        });
        _inputThread.Name = "Chat Input Thread";
        _recvThread = new Thread(() =>
        {
            var buffer = new byte[1024];
            do
            {
                try
                {
                    _cts.Token.ThrowIfCancellationRequested();

                    int recv = _connection.Receive(buffer);
                    if (recv > 0)
                    {
                        var content = Encoding.UTF8.GetString(buffer, 0, recv);
                        int begin = content.IndexOf('<');
                        int end = content.IndexOf('>');
                        string peerName = content.Substring(begin + 1, end - begin - 1);
                        string messageContent = content.Substring(end + 1);
                        
                        var message = new Message(peerName, messageContent);
                        _pending.Enqueue(message);
                    }
                }
                catch (Exception)
                {
                    break;
                }

            } while (true);
        });
        _recvThread.Name = "Chat Receiving Thread";
    }
    
    public void Loop()
    {
        _recvThread.Start();
        _inputThread.Start();

        do
        {
            try
            {
                while (_pending.TryDequeue(out var message))
                {
                    _messageHistory.Add(message);
                    MessageReceived?.Invoke(this, message);
                }
            }
            catch (OperationCanceledException)
            {
                Dispose();
                break;
            }

        } while (true);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _cts.Cancel();

            _inputThread.Join();
            _recvThread.Join();
            
            _cts.Dispose();
        }
    }
}