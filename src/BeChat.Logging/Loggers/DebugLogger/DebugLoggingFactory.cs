using System.Diagnostics;

namespace BeChat.Logging.Loggers.DebugLogger;

internal sealed class DebugLogger : ILogger
{
    private readonly ILogFormatter _formatter;
    private readonly string _category;
    
    public DebugLogger(string category, ILogFormatter formatter)
    {
        _formatter = formatter;
        _category = category;
    }
    
    public void Log(LogLevel level, string message)
    {
        Debug.WriteLine(_formatter.FormatLog(level, _category, message));
    }

    public void Log(LogLevel level, string format, params object?[] args)
    {
        Log(level, String.Format(format, args));
    }

    public void Dispose() {}
}

public sealed class DebugLoggingFactory : ILoggerFactory
{
    private readonly ILogFormatter _formatter;
    
    public DebugLoggingFactory(ILogFormatter formatter)
    {
        _formatter = formatter;
    }
    
    public ILogger CreateLogger(string category)
    {
        return new DebugLogger(category, _formatter);
    }
    
    public void Dispose() {}
}


