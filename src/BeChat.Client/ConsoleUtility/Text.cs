namespace BeChat.Client.ConsoleUtility;

public sealed class Text
{
    private int _x;
    private int _y;
    private bool _drawn;
    private readonly string _content;
    
    public Text(string value)
    {
        _content = value;
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
        
        Console.Write(_content);
    }

    public void Clear()
    {
        if (!_drawn)
        {
            return;
        }

        _drawn = false;
        Console.SetCursorPosition(_x, _y);
        Console.Write(new string(' ', _content.Length));
        Console.SetCursorPosition(_x, _y);
    }
}