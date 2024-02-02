using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace BeChat.Client.ConsoleUtility;

public sealed class ConsoleSelector
{
    private static readonly string[] YesNoOptions = new string[] { "Yes", "No" };
    
    private int _x;
    private int _y;

    private int _xTitle;
    private int _yTitle;

    private int _xOpts;
    private int _yOpts;

    private int _yBottom;
    private int _selectIdx;

    private bool _isDrawn;
    
    private SpinLock _sl = new();

    private readonly ObservableCollection<string> _opts;
    private string _title;
    private bool _titleless;

    public record struct Result(string Option, int Index);

    public event EventHandler<Result>? Prompted; 

    public ConsoleSelector(string? title) : this(title, new ObservableCollection<string>())
    {}
    
    public ConsoleSelector(string? title, ObservableCollection<string> options, int defaultOpt = -1)
    {
        if (title is null)
        {
            _titleless = true;
            _title = "";
        }
        else
        {
            _titleless = false;
            _title = title;
        }

        _selectIdx = defaultOpt;
        _opts = options;

        options.CollectionChanged += OptionsOnCollectionChanged;
    }

    public ObservableCollection<string> Items => _opts;

    public string Title
    {
        get => _title;
        set
        {
            if (_titleless) return;
            
            bool locked = false;
            try
            {
                if (!_sl.IsHeldByCurrentThread)
                {
                    _sl.TryEnter(ref locked);
                }
                
                if (_title.Equals(value)) return;

                int prevLen = _title.Length;
                _title = value;

                if (_isDrawn)
                {
                    var prev = Console.GetCursorPosition();
                    Console.SetCursorPosition(_xTitle, _yTitle);
                    Console.Write(new string(' ', prevLen));
                    Console.SetCursorPosition(_xTitle, _yTitle);
                    Console.Write(_title);
                    Console.SetCursorPosition(prev.Left, prev.Top);
                }
            }
            finally
            {
                if (locked) _sl.Exit();
            }
        }
    }
    
    private void OptionsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        bool locked = false;
        try
        {
            if (!_sl.IsHeldByCurrentThread)
            {
                _sl.TryEnter(ref locked);
            }
            

            if (_isDrawn)
            {
                RedrawOptions();
            }
        }
        finally
        {
            if (locked) _sl.Exit();
        }
    }

    public ConsoleSelector(string? title, string[] options, int defaultOpt = -1) : this(title, new ObservableCollection<string>(options), defaultOpt) 
    {}
    
    
    public void Draw()
    {
        bool locked = false;
        try
        {
            if (!_sl.IsHeldByCurrentThread)
            {
                _sl.TryEnter(ref locked);
            }

            _isDrawn = true;

            Console.CursorVisible = false;
            if (Console.CursorLeft > 0)
            {
                Console.WriteLine();
            }

            _x = Console.CursorLeft;
            _y = Console.CursorTop;

            // Title
            _xTitle = _x;
            _yTitle = _y;
            
            if (!_titleless)
            {
                Console.Write("- ");
                Console.Write(_title);
                Console.WriteLine();
            }

            _xOpts = Console.CursorLeft;
            _yOpts = Console.CursorTop;

            // Options
             RedrawOptions();

            _yBottom = Console.CursorTop;
        }
        finally
        {
            if (locked) _sl.Exit();
        }
    }

    private const string Selector = " > ";
    private const string Padding  = "   ";

    public void Close()
    {
        if (!_isDrawn)
        {
            return;
        }
        
        bool locked = false;
        try
        {
            if (!_sl.IsHeldByCurrentThread)
            {
                _sl.TryEnter(ref locked);
            }

            _isDrawn = false;

            Console.CursorVisible = true;
            Console.CursorTop = _yBottom;
            Console.CursorLeft = _x;
            while (Console.CursorTop != _yTitle)
            {
                Console.Write(new string(' ', Console.BufferWidth - 1));
                Console.CursorTop = Math.Max(0, Console.CursorTop - 1);
                Console.CursorLeft = _x;
            }

            // Erase title
            Console.Write(new string(' ', Console.BufferWidth - 1));
            Console.CursorLeft = _x;
            _selectIdx = -1;
        }
        finally
        {
            if (locked) _sl.Exit();
        }
    }

    private void RedrawOptions()
    {
        bool locked = false;
        try
        {
            if (!_sl.IsHeldByCurrentThread)
            {
                _sl.TryEnter(ref locked);
            }

            if (_opts.Count == 0)
            {
                Console.CursorTop = _yBottom;
                Console.CursorLeft = _x;
                while (Console.CursorTop != _yTitle)
                {
                    Console.Write(new string(' ', Console.BufferWidth - 1));
                    Console.CursorTop = Math.Max(0, Console.CursorTop - 1);
                    Console.CursorLeft = _x;
                }
                
                _yBottom = Console.CursorTop;
                return;
            }
            
            if (_selectIdx == -1)
            {
                _selectIdx = 0;
            }

            void WriteOptionAtIdx(int i)
            {
                Console.Write(new string(' ', Console.BufferWidth - 1));
                Console.CursorLeft = _xOpts;
                Console.Write(_selectIdx == i ? Selector : Padding);
                Console.Write(_opts[i]);
            }
            
            Console.SetCursorPosition(_xOpts, _yOpts);
            WriteOptionAtIdx(0);
            for (int i = 1; i < _opts.Count; ++i)
            {
                Console.WriteLine();
                WriteOptionAtIdx(i);
            }

            _yBottom = Console.CursorTop;
        }
        finally
        {
            if (locked) _sl.Exit();
        }
    }

    public void ConsoleInput(ConsoleKeyInfo key)
    {
        bool locked = false;
        try
        {
            _sl.Enter(ref locked);
            
            void WriteSelector(int prevPos, int newPos)
            {
                Console.SetCursorPosition(_xOpts, _yOpts + prevPos);
                Console.Write(Padding);
                
                Console.SetCursorPosition(_xOpts, _yOpts + newPos);
                Console.Write(Selector);
            }

            int prevPos;
            switch (key.Key)
            {
                case ConsoleKey.DownArrow:
                    prevPos = _selectIdx;
                    _selectIdx = (_selectIdx + 1) % _opts.Count;
                    WriteSelector(prevPos, _selectIdx);
                    break;
                
                case ConsoleKey.UpArrow:
                    prevPos = _selectIdx;
                    _selectIdx--;
                    if (_selectIdx < 0)
                    {
                        _selectIdx = _opts.Count - 1;
                    }
                    WriteSelector(prevPos, _selectIdx);
                    break;
                
                case ConsoleKey.Enter:
                    Prompted?.Invoke( this, new Result(_opts[_selectIdx], _selectIdx) );
                    break;
            }
        }
        finally
        {
            if (locked) _sl.Exit();
        }
    }
    
    public static bool SelectBool(string title, int defaultOption = -1)
    {
        var option = Select(title, YesNoOptions, defaultOption);
        return option == 0;
    }

    private static int Select(ConsoleSelector selector)
    {
        int opt = -1;
        bool prompted = false;
        selector.Prompted += (_, s) =>
        {
            opt = s.Index;
            prompted = true;
        };

        selector.Draw();
        while (!prompted)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true);
                selector.ConsoleInput(key);
            }
        }
        selector.Close();

        return opt;
    }

    public static int Select(string title, ObservableCollection<string> options, int defaultOption = -1)
    {
        var select = new ConsoleSelector(title, options, defaultOption);
        return Select(select);
    }
    
    public static int Select(string title, string[] options, int defaultOption = -1)
    {
        var select = new ConsoleSelector(title, options, defaultOption);
        return Select(select);
    }
}