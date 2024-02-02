using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using BeChat.Client.ConsoleUtility;
using BeChat.Common.Protocol.V1;
using BeChat.Network;

namespace BeChat.Client.View;

public class ChatView : View
{
    private enum ViewState
    {
        Disconnected,
        WaitingForConnection,
        Connecting,
        Connected
    }

    private NetConnection? _connection;
    private ViewState _state = ViewState.Disconnected;
    private readonly ConsolePrompt _prompt;
    private readonly Thread _sendThread;
    private readonly Thread _recvThread;
    private readonly CancellationTokenSource _cts;
    private List<string> _messageHistory = new();
    private readonly ConcurrentQueue<byte[]> _messagesBuffer = new();
    private readonly ConcurrentQueue<string> _recvMessagesBuffer = new();
    private readonly AsyncConsoleSpinner _connectSpinner;
    
    public ChatView(Window w) : base(w)
    {
        _connectSpinner = new AsyncConsoleSpinner();
        _prompt = new ConsolePrompt(null);
        _prompt.Prompted += PromptOnPrompted;
        _sendThread = new Thread(SenderThread);
        _recvThread = new Thread(ReceiveThread);
        _cts = new CancellationTokenSource();
        w.App.Connections.CollectionChanged += ConnectionsOnCollectionChanged;
    }

    private void ReceiveThread()
    {
        if (_connection is null)
        {
            throw new NullReferenceException();
        }

        var buffer = new byte[1024];
        while (true)
        {
            if (_cts.IsCancellationRequested)
            {
                return;
            }

            try
            {
                int recv = _connection.Receive(buffer);
                _recvMessagesBuffer.Enqueue(Encoding.UTF8.GetString(buffer, 0, recv));

                Parent.EnqueueTask(RedrawMessages);
            }
            catch (Exception)
            {
                // ignore
            }
        }
    }
    
    private void SenderThread()
    {
        if (_connection is null)
        {
            throw new NullReferenceException();
        }

        while (true)
        {
            if (_cts.IsCancellationRequested)
            {
                return;
            }

            try
            {
                while (_messagesBuffer.TryDequeue(out var msg))
                {
                    _connection.Send(msg);
                }
            }
            catch (Exception)
            {
                // ignore
            }
        }
    }

    private void PromptOnPrompted(object? sender, ConsolePrompt.Result e)
    {
        _messagesBuffer.Enqueue(Encoding.UTF8.GetBytes(e.Input));
    }

    private void ConnectionsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Parent.EnqueueTask(() =>
        {
            if (IsVisible && _state == ViewState.WaitingForConnection)
            {
                BeginConnect();
            }
        });
    }

    private void RedrawMessages()
    {
        while (_recvMessagesBuffer.TryDequeue(out var message))
        {
            _messageHistory.Add(message);
        }
        
        int lastIndex = _messageHistory.Count - 1;
        int firstIndex = Math.Max(0, lastIndex - 10); // 5 messages
        int numMessages = lastIndex - firstIndex + 1;

        
        Console.SetCursorPosition(1, 1 + numMessages * 2);
        int yBoundary = Console.CursorTop;
        
        while (numMessages > 0)
        {
            string curMessage = _messageHistory[lastIndex];
            Console.CursorLeft = 1;

            Console.Write(new string(' ', Console.BufferWidth - 3));
            Console.CursorTop--;
            Console.CursorLeft = 1;
            Console.Write(new string(' ', Console.BufferWidth - 3));
            Console.CursorLeft = 1;
            
                
            Console.Write(curMessage);
            Console.CursorTop--;
            --numMessages;
            --lastIndex;
        }

        Console.SetCursorPosition(1, yBoundary + 1);
        
        while (Console.CursorTop != Console.WindowHeight - 1)
        {
            Console.CursorLeft = 1;
            Console.Write(new string(' ', Console.BufferWidth - 3));
            Console.CursorTop++;
        }
    }
    
    public void SetWaitConnection()
    {
        if (_state == ViewState.Disconnected)
        {
            _state = ViewState.WaitingForConnection;
        }
    }

    private void EndConnect(NetConnection connection)
    {
        _connection = connection;
        _state = ViewState.Connected;

        _connectSpinner.Text = "Connected";
        Task.Delay(1000).ContinueWith(_ =>
        {
            Parent.EnqueueTask(ShowDialogueWindow);
        });
    }

    private void ShowDialogueWindow()
    {
        _cts.TryReset();
        _sendThread.Start();
        _recvThread.Start();
        
        _connectSpinner.Stop(clear: true);
        
        // draw frame
        Console.CursorLeft = 0;
        Console.Write(".-");
        Console.Write(new string('=', Console.BufferWidth - 4));
        Console.Write("-.");

        while (Console.CursorTop != Console.WindowHeight - 1)
        {
            Console.Write('|');
            Console.Write(new string(' ', Console.BufferWidth - 2));
            Console.Write('|');
        }

        Console.CursorLeft = 0;
        Console.Write("`-");
        Console.Write(new string('=', Console.BufferWidth - 4));
        Console.Write("-\'");
        
        Console.SetCursorPosition(3, Console.WindowHeight - 3);
        _prompt.Draw();
        _prompt.Focus();
        
        Console.SetCursorPosition(1, 1); // top left corner
    }
    
    private void EndConnectTaskCompleted(Task<NetConnection> task)
    {
        Parent.EnqueueTask(() => 
        {
            if (task.IsCompletedSuccessfully)
            {
                EndConnect(task.Result);
            }
            else
            {
                Parent.ShowError("Connection failed", close: true);
            }
        });
    }

    private void BeginConnect()
    {
        if (_state == ViewState.Connecting)
        {
            return;
        }
        
        _state = ViewState.Connecting;
        
        NetNotifyAcceptConnect connect;
        var conns = Parent.App.Connections;
        var bootstrap = Parent.App.Bootstrap;
        lock (conns)
        {
            connect = conns[0].Data;
        }

        try
        {
            var connectionTask = NetConnectionFactory.Default.TraverseAsync(
                new IPEndPoint(bootstrap.PrivateIps.First(), bootstrap.PublicEndPoints.First().Port), 
                connect.PrivateEp,
                connect.PublicEp
            );
            connectionTask.ContinueWith(EndConnectTaskCompleted);
        }
        catch (Exception)
        {
            _state = ViewState.Disconnected;
            Parent.ShowError("Failed to connect to peer", close: true);
        }
    }
    
    public override void OnShow()
    {
        if (_state == ViewState.WaitingForConnection)
        {
            _connectSpinner.Text = "Waiting connection";
        }
        _connectSpinner.SpinAsync();
    }

    public override void OnClose()
    {
        Console.Clear();
        
        _connectSpinner.Stop(clear: true);
        _state = ViewState.Disconnected;
        _connection?.Dispose();
        _prompt.Close();
        
        _cts.Cancel();
        _sendThread.Join();
        _recvThread.Join();
        
        Console.SetCursorPosition(0, 0);
    }

    public override bool OnKeyboardInput(ConsoleKeyInfo keyInfo)
    {
        _prompt.ConsoleInput(keyInfo);
        return true;
    }

    public override bool OnKeyboardCancel()
    {
        switch (_state)
        {
            
        }
        
        Parent.NavigateBack();
        return true;
    }
    
    public override IEnumerable<View> Views => ImmutableArray<View>.Empty;
}