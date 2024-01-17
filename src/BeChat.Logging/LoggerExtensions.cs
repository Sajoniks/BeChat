namespace BeChat.Logging;

public static class LoggerExtensions
{
    public static void LogTrace(this ILogger logger, string message)
    {
        logger.Log(LogLevel.Trace, message);
    }

    public static void LogTrace(this ILogger logger, string format, params object?[] args)
    {
        logger.Log(LogLevel.Trace, format, args);
    }

    public static void LogInfo(this ILogger logger, string message)
    {
        logger.Log(LogLevel.Log, message);
    }
    
    public static void LogInfo(this ILogger logger, string format, params object?[] args)
    {
        logger.Log(LogLevel.Log, format, args);
    }

    public static void LogWarn(this ILogger logger, string message)
    {
        logger.Log(LogLevel.Warn, message);
    }
    
    public static void LogWarn(this ILogger logger, string format, params object?[] args)
    {
        logger.Log(LogLevel.Warn, format, args);
    }
    
    public static void LogError(this ILogger logger, string message)
    {
        logger.Log(LogLevel.Error, message);
    }
    
    public static void LogError(this ILogger logger, string format, params object?[] args)
    {
        logger.Log(LogLevel.Error, format, args);
    }
}