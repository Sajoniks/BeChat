namespace BeChat.Client.ConsoleUtility;

public interface IConsoleSpinnerWidget
{
    void Spin();
    int Length { get; }
}

public class AnimatedConsoleSpinner : IConsoleSpinnerWidget
{
    public sealed class AnimationSource
    {
        private string[] _frames;

        public AnimationSource(string[] frames)
        {
            if (frames.Length == 0) throw new InvalidDataException();
        
            _frames = new String[frames.Length];
            frames.CopyTo(_frames, 0);
        }
        
        public string this[int index]
        {
            get => _frames[index];
        }

        public int Count => _frames.Length;
    }

    private readonly AnimationSource _animation;
    private int _written = 0;
    private int _spinner = 0;

    public AnimatedConsoleSpinner(AnimationSource source)
    {
        _animation = source;
    }
    
    public void Spin()
    {
        Console.Write(_animation[_spinner]);
        _written = _animation[_spinner].Length;  
        _spinner = (_spinner + 1) % _animation.Count;
    }

    public int Length => _written;
}

public sealed class DotsConsoleSpinner : AnimatedConsoleSpinner
{
    private static readonly AnimationSource Animation;

    static DotsConsoleSpinner()
    {
        Animation = new AnimationSource(new[]
        {
            "", " .", " ..", "...", ".. ", ". "
        });
    }
    
    public DotsConsoleSpinner() : base(Animation) { }

    public static DotsConsoleSpinner Default => new DotsConsoleSpinner();
}

public sealed class SlashConsoleSpinner : AnimatedConsoleSpinner
{
    private static readonly AnimationSource Animation;

    static SlashConsoleSpinner()
    {
        Animation = new AnimationSource(new[]
        {
            " /", " |", " \\", " |"
        });
    }
    
    private SlashConsoleSpinner() : base(Animation)
    {}

    public static SlashConsoleSpinner Default => new SlashConsoleSpinner();
}

public class ConsoleSpinner : IDisposable
{
    private IConsoleSpinnerWidget _widget;
    private int _currentH = 0;
    private int _currentW = 0;
    private int _written = 0;
    private bool _visible = false;

    public ConsoleSpinner() : this(new DotsConsoleSpinner())
    { }

    public ConsoleSpinner(IConsoleSpinnerWidget widget)
    {
        _widget = widget ?? throw new ArgumentNullException(nameof(widget));
        _visible = Console.CursorVisible;
        _currentH = Console.CursorTop;
        _currentW = Console.CursorLeft;
    }

    public void Clear()
    {
        int prevH = Console.CursorTop;
        int prevW = Console.CursorLeft;

        Console.CursorTop = _currentH;
        Console.CursorLeft = _currentW;

        if (_written > 0)
        {
            Console.Write(new string(' ', _written));
            Console.CursorLeft = Math.Max(0, Console.CursorLeft - _written);
            _written = 0;
        }
        
        Console.CursorTop = prevH;
        Console.CursorLeft = prevW;
        Console.CursorVisible = _visible;
    }
    
    public void Spin()
    {
        Console.CursorVisible = false;
        
        int prevH = Console.CursorTop;
        int prevW = Console.CursorLeft;

        Console.CursorTop = _currentH;
        Console.CursorLeft = _currentW;

        if (_written > 0)
        {
            Console.Write(new string(' ', _written));
            Console.CursorLeft = Math.Max(0, Console.CursorLeft - _written);
            _written = 0;
        }

        Console.Write(Text);
        _widget.Spin();
        _written = _widget.Length + Text.Length;
        
        Console.CursorTop = prevH;
        Console.CursorLeft = prevW;
    }
    
    public string Text { get; set; } = "";

    public void Dispose()
    {
        int prevH = Console.CursorTop;
        int prevW = Console.CursorLeft;
        
        Console.CursorTop = _currentH;
        Console.CursorLeft = _currentW;
        if (_written > 0)
        {
            Console.Write(new string(' ', _written));
            _written = 0;
        }
        
        Console.CursorTop = prevH;
        Console.CursorLeft = prevW;

        Console.CursorVisible = _visible;
    }
}

public class AsyncConsoleSpinner : IDisposable
{
    private ConsoleSpinner _wrappee;
    private CancellationTokenSource _cts;
    private AutoResetEvent _resetEvent;
    private int _ms;
    private int _run;
    private bool _disposed;
    
    public AsyncConsoleSpinner() : this(framerate: 100)
    {}
    
    public AsyncConsoleSpinner(int framerate) : this(new ConsoleSpinner(), framerate)
    {}
    
    public AsyncConsoleSpinner(ConsoleSpinner wrappee, int framerate)
    {
        _wrappee = wrappee;
        _resetEvent = new AutoResetEvent(true);
        _cts = new CancellationTokenSource();
        _ms = framerate;
        _run = 0;
    }

    private async void RunInternalAsync(CancellationToken token)
    {
        try
        {
            do
            {
                token.ThrowIfCancellationRequested();
                if (_run == 2)
                {
                    _resetEvent.Set();
                    continue;
                }
                
                _wrappee.Spin();

                _resetEvent.Set();
                await Task.Delay(_ms, token).ConfigureAwait(false);

            } while (true);
        }
        catch (Exception)
        {
            _resetEvent.Set();
            // ignored
        }
    }

    public void SpinAsync(string text)
    {
        Text = text;
        SpinAsync();
    }
    
    public void SpinAsync()
    {
        if (_run == 2)
        {
            Interlocked.Decrement(ref _run);
        }
        else if (_run == 0)
        {
            _run = 1;
            RunInternalAsync(_cts.Token);
        }
    }

    public void Pause()
    {
        if (_run == 1)
        {
            Interlocked.Increment(ref _run);
            _resetEvent.WaitOne();
            _wrappee.Clear();
        }
    }
    
    public void Stop()
    {
        _cts.Cancel();
        _resetEvent.WaitOne();
    }

    public string Text
    {
        set
        {
            _resetEvent.WaitOne();
            _wrappee.Text = value;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            
            Stop();
            _run = 0;
            _cts.Dispose();
            _wrappee.Dispose();
        }
    }
}