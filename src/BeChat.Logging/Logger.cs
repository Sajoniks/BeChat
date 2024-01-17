
namespace BeChat.Logging;

public interface ILogger : IDisposable
{
    void Log(LogLevel level, string message);
    void Log(LogLevel level, string format, params object?[] args);
}