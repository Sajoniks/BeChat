namespace BeChat.Client.ConsoleUtility;


public sealed class ConsolePrompt
{
    public struct Result
    {
        public Result() { }
        
        public string Input { get; init; } = "";
        public bool Intercept { get; init; } = false;
    }
    
    public sealed class Settings
    {
        public string? Description { get; init; } = null;
        public string? ValidInput { get; init; } = null;
        public bool InterceptSigsev { get; init; } = false;
        public Func<char, char>? Modifier { get; init; } = null;
        public Func<string, bool>? Validator { get; init; } = null;
    }
    
    private static readonly Settings Default = new();

    private readonly string? _title;
    private readonly Settings _settings;
    private int _x;
    private int _y;
    private int _xCursor;
    private int _yCursor;
    private int _xWrite;
    private int _yWrite;
    private int _yBottom;
    private bool _interrupt;
    private bool _drawn = false;
    private bool _focused = false;
    private Stack<char> _buffer = new();
    
    public event EventHandler<Result>? Prompted; 

    public ConsolePrompt(string? title) : this(title, Default)
    {}

    public ConsolePrompt(string? title, ConsolePrompt.Settings settings)
    {
        _title = title;
        _settings = settings;
    }

    public void Focus()
    {
        if (!_focused)
        {
            _focused = true;
            Console.SetCursorPosition(_xCursor, _yCursor);
            Console.Write("  > ");
            Console.SetCursorPosition(_xWrite + _buffer.Count, _yWrite);
        }
    }

    public void Unfocus()
    {
        if (_focused)
        {
            Console.SetCursorPosition(_xCursor, _yCursor);
            Console.Write("    ");
            Console.SetCursorPosition(_xWrite + _buffer.Count, _yWrite);
            _focused = false;
        }
    }
    
    public void Draw()
    {
        if (_drawn)
        {
            return;
        }

        _drawn = true;
        if (Console.CursorLeft > 0)
        {
            Console.WriteLine();
        }
        
        _x = Console.CursorLeft;
        _y = Console.CursorTop;

        ConsoleInterface.Instance.CursorVisible = false;

        if (_title is not null)
        {
            // Title
            Console.Write("- ");
            Console.Write(_title);
            Console.WriteLine();
        }

        // Prompt area
        _xCursor = Console.CursorLeft;
        _yCursor = Console.CursorTop;
        
        Console.Write("    ");
        
        _xWrite = Console.CursorLeft;
        _yWrite = Console.CursorTop;

        if (_buffer.Count != 0)
        {
            Console.Write(_buffer.Reverse().ToArray());
        }
        
        _yBottom = _yWrite;
    }

    private void OnCanceled(object? o, ConsoleCancelEventArgs args)
    {
        args.Cancel = true;
        _interrupt = true;
    }
    
    public void Close()
    {
        if (!_drawn)
        {
            return;
        }

        _drawn = false;
        _focused = false;
        
        Console.CursorTop = _yBottom;
        while (Console.CursorTop != _y)
        {
            Console.CursorLeft = 0;
            Console.Write(new string(' ', Console.BufferWidth));
            Console.CursorTop = Math.Max(0, Console.CursorTop - 2);
        }
        
        Console.CursorLeft = _x;
        Console.Write(new string(' ', Console.BufferWidth - 1 - _x));

        Console.CursorLeft = _x;
        Console.CursorTop= _y;
        ConsoleInterface.Instance.CursorVisible = true;
    }

    public string CopyString()
    {
        if (_buffer.Any())
        {
            return String.Join("", _buffer.Reverse());
        }

        return "";
    }

    public void ClearBuffer()
    {
        int len = _buffer.Count;
        if (len > 0)
        {
            _buffer.Clear();
            Console.SetCursorPosition(_xWrite, _yWrite);
            Console.Write(new string(' ', len));
            Console.SetCursorPosition(_xWrite, _yWrite);
        }
    }
    
    public void ConsoleInput(ConsoleKeyInfo key)
    {
        Focus();

        bool prompted = false;
        string? input = null;
        switch (key.Key)
        {
            case ConsoleKey.Enter:
                if (_buffer.Any())
                {
                    input = CopyString();
                    _buffer.Clear();
                    Console.SetCursorPosition(_xWrite, _yWrite);
                    Console.Write(new string(' ', input.Length));
                    Console.SetCursorPosition(_xWrite, _yWrite);
                }
                
                prompted = true;
                break;

            case ConsoleKey.Backspace:
                if (_buffer.Any())
                {
                    _buffer.Pop();
                    Console.SetCursorPosition(_xWrite + _buffer.Count, _yWrite);
                    Console.Write(' ');
                    Console.SetCursorPosition(_xWrite + _buffer.Count, _yWrite);
                }

                break;

            default:
            {
                var ch = key.KeyChar;
                ch = _settings.Modifier?.Invoke(ch) ?? ch;

                bool discard = false;
                if (_settings.ValidInput is not null)
                {
                    discard = !_settings.ValidInput.Contains(ch);
                }
                else
                {
                    discard = char.IsControl(ch);
                }

                if (!discard)
                {
                    if (_xWrite + _buffer.Count < Console.BufferWidth)
                    {
                        Console.SetCursorPosition(_xWrite + _buffer.Count, _yWrite);
                        _buffer.Push(ch);
                        Console.Write(ch);
                    }
                }
            }
                break;
        }

        
        if (prompted)
        {
            bool validated = input is not null && (_settings.Validator?.Invoke(input) ?? true);
            if (validated)
            {
                Prompted?.Invoke(this, new Result
                {
                    Input = input!,
                    Intercept = false
                });
            }
        }
    }
    
    public static Result Prompt(string title)
    {
        return Prompt(title, Default);
    }
    
    public static Result Prompt(string title, Settings settings)
    {
        bool prompted = false;
        Result? r = null;
        
        var prompt = new ConsolePrompt(title, settings);
        prompt.Prompted += (_, args) =>
        {
            prompted = true;
            r = args;
        };
        prompt.Draw();
        do
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true);
                prompt.ConsoleInput(key);
            }
        } while (!prompted);
        prompt.Close();

        if (r is null)
        {
            throw new InvalidProgramException();
        }
        
        return r.Value;
    }
}