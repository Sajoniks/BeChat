using System.Collections.Concurrent;

namespace BeChat.Logging;

internal sealed class CompoundLogger : ILogger
{
    private ILogger[] _loggers;
    public CompoundLogger(string category, IEnumerable<ILogger> loggers)
    {
        _loggers = loggers.ToArray();
    }
    
    public void Dispose()
    {
        foreach (var logger in _loggers)
        {
            logger.Dispose();
        }
    }

    public void Log(LogLevel level, string message)
    {
        foreach (var logger in _loggers)
        {
            logger.Log(level, message);
        }
    }

    public void Log(LogLevel level, string format, params object?[]? args)
    {
        if (args is null)
        {
            Log(level, format);
        }
        else
        {
            foreach (var logger in _loggers)
            {
                logger.Log(level, format, args);
            }
        }
    }
}

public class LoggerFactory : ILoggerFactory
{
    private readonly ILoggerFactory[] _factories;
    private readonly Dictionary<string, ILogger> _loggers = new();

    public LoggerFactory(IEnumerable<ILoggerFactory> factories)
    {
        _factories = factories.ToArray();
    }
    
    public ILogger CreateLogger(string category)
    {
        if (!_loggers.TryGetValue(category, out var logger))
        {
            List<ILogger> loggers = new();
            foreach (var factory in _factories)
            {
                loggers.Add(factory.CreateLogger(category));
            }

            logger = new CompoundLogger(category, loggers);
            _loggers[category] = logger;
        }

        return logger;
    }

    public void Dispose()
    {
        
    }
}