namespace BeChat.Client.ConsoleUtility;

public class ConsolePrompt
{
    private static Func<string, bool> Null = (_) => true;

    public static string Prompt(string title)
    {
        return Prompt(title, Null);
    }

    public static string Prompt(string title, Func<string, bool> validation)
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
        Console.Write("  > ");

        string? input = null;
        do
        {
            input = Console.ReadLine();
        } while (input is null || !validation(input));
        
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
        return input;
    }
}