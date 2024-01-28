namespace BeChat.Client.ConsoleUtility;


public class ConsolePrompt
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

    public static Result Prompt(string title)
    {
        return Prompt(title, Default);
    }
    
    public static Result Prompt(string title, Settings settings)
    {
        bool visible = Console.CursorVisible;
        Console.CursorVisible = true;
        int x = Console.CursorLeft;
        int y = Console.CursorTop;
        
        if (Console.CursorLeft > 0)
        {
            Console.WriteLine();
        }
        
        Console.Write("- ");
        Console.WriteLine(title);

        // @todo
        if (settings.Description is not null)
        {
            string desc = settings.Description;
            
            Console.Write("  * ");
            int offset = Console.CursorLeft;
            int lineLength = Console.BufferWidth - offset - 1;

            for (int i = 0; i < desc.Length; i += lineLength)
            {
                ReadOnlySpan<char> span = settings.Description.AsSpan(i, Math.Min(i + lineLength, desc.Length));
                foreach (var ch in span)
                {
                    Console.Write(ch);
                }
                Console.WriteLine();
            }
        }
        
        Console.Write("  > ");

        Stack<char> buffer = new Stack<char>();
        
        string? input = null;
        bool prompted = false;
        bool interrupt = false;
        bool validated = false;

        void OnCanceled(object? o, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            interrupt = true;
        }

        bool prevFlag = Console.TreatControlCAsInput;
        if (settings.InterceptSigsev)
        {
            Console.TreatControlCAsInput = false;
            Console.CancelKeyPress += OnCanceled;
        }

        int pos = Console.CursorLeft;
        do
        {
            do
            {
                if (interrupt)
                {
                    input = "";
                    break;
                }
                
                if (!Console.KeyAvailable)
                {
                    continue;
                }

                var key = Console.ReadKey(intercept: true);
                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        if (buffer.Any())
                        {
                            input = String.Join("", buffer.Reverse());
                            buffer.Clear();
                            Console.CursorLeft = pos;
                            Console.Write(new string(' ', input.Length));
                            Console.CursorLeft = pos;
                        }

                        prompted = true;

                        break;

                    case ConsoleKey.Backspace:
                        if (buffer.Any())
                        {
                            buffer.Pop();
                            Console.CursorLeft--;
                            Console.Write(' ');
                            Console.CursorLeft--;
                        }

                        break;

                    default:
                    {
                        var ch = key.KeyChar;
                        ch = settings.Modifier?.Invoke(ch) ?? ch;

                        bool discard = false;
                        if (settings.ValidInput is not null)
                        {
                            discard = !settings.ValidInput.Contains(ch);
                        }
                        else
                        {
                            discard = char.IsControl(ch);
                        }

                        if (!discard)
                        {
                            buffer.Push(ch);
                            Console.Write(ch);
                        }
                    }
                        break;
                }
            } while (!prompted);

            validated = input is not null && (settings.Validator?.Invoke(input) ?? true);
            
        } while (!interrupt && !validated);

        if (settings.InterceptSigsev)
        {
            Console.TreatControlCAsInput = prevFlag;
            Console.CancelKeyPress -= OnCanceled;
        }
        
        while (Console.CursorTop != y)
        {
            Console.CursorLeft = 0;
            Console.Write(new string(' ', Console.BufferWidth));
            Console.CursorTop = Math.Max(0, Console.CursorTop - 2);
        }

        Console.CursorLeft = x;
        Console.Write(new string(' ', Console.BufferWidth - 1 - x));

        Console.CursorLeft = x;
        Console.CursorTop= y;
        Console.CursorVisible = visible;
        return new Result
        {
            Input = input!,
            Intercept = interrupt
        };
    }
}