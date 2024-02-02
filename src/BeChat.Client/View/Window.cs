namespace BeChat.Client.View;


public sealed class Window
{
    private CancellationTokenSource _cts;
    private CancellationTokenRegistration _reg;
    private Thread _thread;
    private SemaphoreSlim _semaphore;
    private bool _cancellationRequested = false;
    private Queue<View> _nextView = new();
    private Queue<Action> _queuedActions = new();
    private Stack<View> _views = new();

    public bool IsVisible(View v)
    {
        return CurrentView == v;
    }

    public View MainMenuView { get; }
    public View LoginView { get; }
    public View ExitView { get; }
    public View AddFriendView { get; }
    public View FriendListView { get; }
    public View ProfileView { get; }
    public View ChatView { get; }
    public View NotificationsView { get; }
    public BeChatApplication App { get; }

    
    private View? CurrentView => _views.Any() ? _views.Peek() : null;

    private void ThreadJob()
    {
        void ConsoleCancel(object? _, ConsoleCancelEventArgs args)
        {
            _cancellationRequested = true;
            args.Cancel = true;
        }

        Console.TreatControlCAsInput = false;
        Console.CancelKeyPress += ConsoleCancel;

        do
        {
            try
            {
                Console.SetWindowPosition(0, 0);
            }
            catch (Exception)
            {
                // ignore
            }

            _semaphore.Wait();
            if (_nextView.TryDequeue(out var view))
            {
                SetView(view);
            }

            while (_queuedActions.TryDequeue(out var action))
            {
                action.Invoke();
            }
            _semaphore.Release();

            var current = CurrentView;
            if (current is null) continue;

            if (_cancellationRequested)
            {
                _cancellationRequested = false;
                if (!current.OnKeyboardCancel())
                {
                    _cts.Cancel();
                }
            }
            else if (Console.KeyAvailable)
            {
                foreach (var view1 in _views)
                {
                    if (view1.OnKeyboardInput(Console.ReadKey(true)))
                    {
                        break;
                    }
                }
            }

        } while (!_cts.IsCancellationRequested);

        Console.TreatControlCAsInput = true;
        Console.CancelKeyPress -= ConsoleCancel;
    }

    public Window(BeChatApplication app)
    {
        App = app;

        MainMenuView = new MainMenuView(this);
        LoginView = new LoginView(this);
        ExitView = new ExitView(this);
        AddFriendView = new AddFriendView(this);
        FriendListView = new FriendListView(this);
        ProfileView = new ProfileView(this);
        ChatView = new ChatView(this);
        NotificationsView = new NotificationsView(this);
        
        _cts = new CancellationTokenSource();
        _reg = _cts.Token.Register(() =>
        {
            Environment.Exit(0);
        });
        
        _semaphore = new SemaphoreSlim(1, 1);
        _thread = new Thread(ThreadJob)
        {
            Name = "Console UI Thread"
        };
        _thread.Start();
    }

    public Task EnqueueTask(Action x)
    {
        TaskCompletionSource tcs = new();
        if (Thread.CurrentThread.ManagedThreadId == _thread.ManagedThreadId)
        {
            _queuedActions.Enqueue(() =>
            {
                x.Invoke();
                tcs.SetResult();
            });
        }
        else
        {
            _semaphore.Wait();
            _queuedActions.Enqueue(() =>
            {
                x.Invoke();
                tcs.SetResult();
            });
            _semaphore.Release();
        }

        return tcs.Task;
    }

    public void ShowError(string error, bool close)
    {
        var view = new ErrorView(this, error);
        SetView(view, close);
    }
    
    public void ShowError(string error)
    {
        var view = new ErrorView(this, error);
        SetView(view);
    }
    
    public void CloseAll()
    {
        if (Thread.CurrentThread.ManagedThreadId != _thread.ManagedThreadId)
        {
            EnqueueTask(CloseAll).GetAwaiter().GetResult();
        }
        else
        {
            foreach (var view in _views)
            {
                view.OnClose();
            }

            _views.Clear();
            Console.Clear();
        }
    }
    
    public void RequestExit()
    {
        CloseAll();
        _cts.Cancel();
        _cts.Dispose();
    }
    
    public void NavigateBack()
    {
        if (_views.Any())
        {
            _views.Peek().OnClose();
            _views.Pop();
        }
        _views.Peek().OnShow();
    }

    public void WaitUntilExit()
    {
        _thread.Join();
    }
    
    public void SetView(View view, bool deleteCurrent = false)
    {
        if (Thread.CurrentThread.ManagedThreadId != _thread.ManagedThreadId)
        {
            _semaphore.Wait();
            _nextView.Enqueue(view);
            _semaphore.Release();
            
            return;
        }

        if (_views.Any())
        {
            _views.Peek().OnClose();
            if (deleteCurrent)
            {
                _views.Pop();
            }
        }

        Console.Clear();
        
        _views.Push(view);
        view.OnShow();
    }
}