namespace BeChat.Client.ConsoleUtility;

public class ConsoleSelector
{
    public static string Select(string title, string[] options)
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
        
        for (int i = 0; i < options.Length; ++i)
        {
            if (i == 0)
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
        int curSelectionIdx = 0;
        bool prompted = false;
        do
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.UpArrow)
            {
                Console.SetCursorPosition(titleX, titleY + curSelectionIdx + 1);
                Console.Write("   ");
                curSelectionIdx = Math.Max(0, curSelectionIdx - 1);
                Console.SetCursorPosition(titleX, titleY + curSelectionIdx + 1);
                Console.Write("  >");
            }
            else if (key.Key == ConsoleKey.DownArrow)
            {
                Console.SetCursorPosition(titleX, titleY + curSelectionIdx + 1);
                Console.Write("   ");
                curSelectionIdx = Math.Min(options.Length - 1, curSelectionIdx + 1);
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

        return options[curSelectionIdx];
    }
}