namespace BeChat.Logging;

public interface ILoggerFactory : IDisposable
{
    public ILogger CreateLogger(string category);
}