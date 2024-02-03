using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using BeChat.Client.ConsoleUtility;
using BeChat.Common.Protocol;
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
    private readonly CancellationTokenSource _cts;
    private Thread _receiverThread;
    private List<NetNotifyChatMessage> _messageHistory = new();
    private readonly ConcurrentQueue<string> _recvMessagesBuffer = new();
    private readonly AsyncConsoleSpinner _connectSpinner;
    
    public ChatView(Window w) : base(w)
    {
        _connectSpinner = new AsyncConsoleSpinner();
        _prompt = new ConsolePrompt(null);
        _prompt.Prompted += PromptOnPrompted;
        _receiverThread = new Thread(ReceiverThread);
        _receiverThread.Name = "ChatView Receiver Thread";
        _cts = new CancellationTokenSource();
        w.App.Connections.CollectionChanged += ConnectionsOnCollectionChanged;
    }

    private void ReceiverThread()
    {
        byte[] buffer = new byte[2048];
        while (!_cts.IsCancellationRequested)
        {
            int recv;
            try
            {
                recv = _connection!.Receive(buffer);
            }
            catch (Exception)
            {
                return;
            }

            NetNotifyChatMessage message;
            try
            {
                using var reader = new NetMessageReader(buffer.AsSpan(0, recv));
                message = NetMessage<NetNotifyChatMessage>.Read(reader);
            }
            catch (Exception)
            {
                continue;
            }

            Parent.EnqueueTask(() =>
            {
                _messageHistory.Add(message);
                RedrawMessages();
            });
        }
    }
    
    private void PromptOnPrompted(object? sender, ConsolePrompt.Result e)
    {
        NetNotifyChatMessage message = new()
        {
            UserId = Parent.App.Authorization.CurrentUser!.Id,
            Content = e.Input,
            Timestamp = DateTime.UtcNow
        };
        _messageHistory.Add(message);
        _ = _connection!.SendAsync(NetMessage<NetNotifyChatMessage>.WriteBytes(message));
        
        RedrawMessages();
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
        int lastIndex = _messageHistory.Count - 1;
        int firstIndex = Math.Max(0, lastIndex - 15);

        Console.CursorTop = 1;
        for (int i = firstIndex; i <= lastIndex; ++i)
        {
            NetNotifyChatMessage curMessage = _messageHistory[i];
            Console.CursorLeft = 1;
            
            bool isOurs = false;
            bool isSame = false;
            
            IUser? user = Parent.App.ContactList.FirstOrDefault(x => x.Id.Equals(curMessage.UserId));
            if (user is null)
            {
                user = Parent.App.Authorization.CurrentUser!.Id.Equals(curMessage.UserId)
                    ? Parent.App.Authorization.CurrentUser
                    : null;
                isOurs = true;
            }   
            
            if (user is null)
            {
                continue;
            }

            if (i > 0 && _messageHistory[i - 1].UserId.Equals(user.Id))
            {
                isSame = true;
            }

            int yStart = Console.CursorTop;
            Console.Write(new string(' ', Console.BufferWidth - 2));
            Console.CursorTop++;
            Console.CursorLeft = 1;
            Console.Write(new string(' ', Console.BufferWidth - 2));
            Console.CursorTop++;
            Console.CursorLeft = 1;
            Console.Write(new string(' ', Console.BufferWidth - 2));
            Console.CursorTop = yStart;
            Console.CursorLeft = 1;
            
            if (isOurs)
            {
                if (!isSame)
                {
                    Console.CursorLeft = Console.BufferWidth - 3 - user.UserName.Length;
                    Console.Write('<');
                    Console.Write(user.UserName);
                    Console.Write('>');
                    Console.CursorTop++;
                }

                Console.CursorLeft = Console.BufferWidth - 3 - curMessage.Content.Length;
                Console.Write(curMessage.Content);
                Console.CursorTop++;
            }
            else
            {
                if (!isSame)
                {
                    Console.CursorLeft = 1;
                    Console.Write('<');
                    Console.Write(user.UserName);
                    Console.Write('>');
                    Console.CursorTop++;
                }

                Console.CursorLeft = 2;
                Console.Write(curMessage.Content);
                Console.CursorTop++;
            }
        }
        
        while (Console.CursorTop < Console.WindowHeight - 2)
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
        _receiverThread.Start();
        
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
                // connect.PrivateEp,
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
        _receiverThread.Join();
        
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