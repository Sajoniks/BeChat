using System.Runtime.InteropServices;

namespace BeChat.Client.ConsoleUtility;

sealed class WindowsConsoleInterface : IConsoleInterface
{
    public bool CursorVisible
    {
        get
        {
#pragma warning disable CA1416
            return Console.CursorVisible;
#pragma warning restore CA1416
        }
        set
        {
            Console.CursorVisible = value;
        }
    }
}

sealed class LinuxConsoleInterface : IConsoleInterface
{
    public bool CursorVisible { get; set; }
}

public static class ConsoleInterface
{
    private static IConsoleInterface? _instance;
    
    public static IConsoleInterface Instance
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _instance = new WindowsConsoleInterface();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                _instance = new LinuxConsoleInterface();
            }
            else
            {
                throw new NotSupportedException();
            }

            return _instance;
        }
    }
}