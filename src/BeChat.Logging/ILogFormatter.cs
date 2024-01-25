namespace BeChat.Logging;

public interface ILogFormatter
{
    public string FormatLog(LogLevel level, string category, string message);
}