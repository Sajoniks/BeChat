namespace BeChat.Logging;

public interface ILogFormatter
{
    public ReadOnlyMemory<char> FormatLog(LogLevel level, string category, string message);
}