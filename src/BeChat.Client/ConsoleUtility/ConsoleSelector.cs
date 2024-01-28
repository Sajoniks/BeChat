using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace BeChat.Client.ConsoleUtility;

public class ConsoleSelector
{
    public static bool SelectBool(string title, int defaultOption = -1)
    {
        var option = Select(title, new string[] { "yes", "no" }, defaultOption);
        return option == 0;
    }

    public static int Select(string title, ObservableCollection<string> options, int defaultOption = -1)
    {
        bool visible = Console.CursorVisible;
        Console.CursorVisible = false;
        int x = Console.CursorLeft;
        int y = Console.CursorTop;
        
        if (Console.CursorLeft > 0)
        {
            Console.WriteLine();
        }
        
        int titleX = Console.CursorLeft;
        int titleY = Console.CursorTop;
        
        Console.Write("- ");
        Console.WriteLine(title);

        bool redrawList = false;
        var sl = new SpinLock();
        void OptionsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            bool locked = false;
            try
            {
                sl.Enter(ref locked);
                redrawList = true;
            }
            finally
            {
                if (locked) sl.Exit();
            }
        }
        options.CollectionChanged += OptionsOnCollectionChanged;
        
        int curSelectionIdx = 0;
        if (defaultOption > 0 && defaultOption < options.Count)
        {
            curSelectionIdx = defaultOption;
        }

        int optionsX = Console.CursorLeft;
        int optionsY = Console.CursorTop;
        
        for (int i = 0; i < options.Count; ++i)
        {
            if (i == curSelectionIdx)
            {
                Console.Write("  > ");
            }
            else
            {
                Console.Write("    ");
            }

            Console.WriteLine(options[i]);
        }
        int endY = Console.CursorTop;
        bool prompted = false;

        do
        {
            bool locked = false;
            try
            {
                sl.Enter(ref locked);
                
                if (redrawList)
                {
                    Console.SetCursorPosition(optionsX, optionsY);
                    for (int i = 0; i < options.Count; ++i)
                    {
                        Console.WriteLine(new string(' ', Console.BufferWidth - 1));
                        Console.CursorTop--;

                        if (i == curSelectionIdx)
                        {
                            Console.Write("  > ");
                        }
                        else
                        {
                            Console.Write("    ");
                        }

                        Console.WriteLine(options[i]);
                    }
                }
            }
            finally
            {
                curSelectionIdx = Math.Min(curSelectionIdx, options.Count - 1);
                redrawList = false;
                if (locked) sl.Exit();
            }

            if (!Console.KeyAvailable)
            {
                continue;
            }
            
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.UpArrow)
            {
                Console.SetCursorPosition(titleX, titleY + curSelectionIdx + 1);
                Console.Write("   ");
                curSelectionIdx--;
                if (curSelectionIdx < 0)
                {
                    curSelectionIdx = options.Count - 1;
                }
                Console.SetCursorPosition(titleX, titleY + curSelectionIdx + 1);
                Console.Write("  >");
            }
            else if (key.Key == ConsoleKey.DownArrow)
            {
                Console.SetCursorPosition(titleX, titleY + curSelectionIdx + 1);
                Console.Write("   ");
                curSelectionIdx = (curSelectionIdx + 1) % options.Count;
                Console.SetCursorPosition(titleX, titleY + curSelectionIdx + 1);
                Console.Write("  >");
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                prompted = true;
            }

        } while (!prompted);

        Console.CursorTop = endY;
        while (Console.CursorTop != y)
        {
            Console.CursorLeft = 0;
            Console.Write(new string(' ', Console.BufferWidth));
            Console.CursorTop = Math.Max(0, Console.CursorTop - 2);
        }
        
        Console.CursorLeft = x;
        Console.Write(new string(' ', Console.BufferWidth - 1 - x));

        Console.CursorLeft = x;
        Console.CursorTop = y;
        Console.CursorVisible = visible;

        options.CollectionChanged -= OptionsOnCollectionChanged;

        return curSelectionIdx;
    }
    
    public static int Select(string title, string[] options, int defaultOption = -1)
    {
        if (options.Length == 0)
        {
            throw new ArgumentException();
        }
        
        bool visible = Console.CursorVisible;
        Console.CursorVisible = false;
        int x = Console.CursorLeft;
        int y = Console.CursorTop;
        
        if (Console.CursorLeft > 0)
        {
            Console.WriteLine();
        }

        int titleX = Console.CursorLeft;
        int titleY = Console.CursorTop;
        
        Console.Write("- ");
        Console.WriteLine(title);
        
        int curSelectionIdx = 0;
        if (defaultOption > 0 && defaultOption < options.Length)
        {
            curSelectionIdx = defaultOption;
        }
        
        for (int i = 0; i < options.Length; ++i)
        {
            if (i == curSelectionIdx)
            {
                Console.Write("  > ");
            }
            else
            {
                Console.Write("    ");
            }

            Console.WriteLine(options[i]);
        }

        int endY = Console.CursorTop;
        bool prompted = false;
        do
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.UpArrow)
            {
                Console.SetCursorPosition(titleX, titleY + curSelectionIdx + 1);
                Console.Write("   ");
                curSelectionIdx--;
                if (curSelectionIdx < 0)
                {
                    curSelectionIdx = options.Length - 1;
                }
                Console.SetCursorPosition(titleX, titleY + curSelectionIdx + 1);
                Console.Write("  >");
            }
            else if (key.Key == ConsoleKey.DownArrow)
            {
                Console.SetCursorPosition(titleX, titleY + curSelectionIdx + 1);
                Console.Write("   ");
                curSelectionIdx = (curSelectionIdx + 1) % options.Length;
                Console.SetCursorPosition(titleX, titleY + curSelectionIdx + 1);
                Console.Write("  >");
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                prompted = true;
            }

        } while (!prompted);

        Console.CursorTop = endY;
        while (Console.CursorTop != y)
        {
            Console.CursorLeft = 0;
            Console.Write(new string(' ', Console.BufferWidth));
            Console.CursorTop = Math.Max(0, Console.CursorTop - 2);
        }
        
        Console.CursorLeft = x;
        Console.Write(new string(' ', Console.BufferWidth - 1 - x));

        Console.CursorLeft = x;
        Console.CursorTop = y;
        Console.CursorVisible = visible;

        return curSelectionIdx;
    }
}