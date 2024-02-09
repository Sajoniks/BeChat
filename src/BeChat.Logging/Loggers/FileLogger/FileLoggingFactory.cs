using System.Diagnostics;
using System.Reflection;

namespace BeChat.Logging.Loggers.FileLogger;

internal sealed class StreamLogger : ILogger
{
    private readonly StreamWriter _writer;
    private readonly string _category;
    private readonly ILogFormatter _formatter;
    private static SpinLock _lock = new();

    public StreamLogger(StreamWriter writer, string category, ILogFormatter formatter)
    {
        _writer = writer;
        _formatter = formatter;
        _category = category;
    }

    public void Dispose()
    { }

    public void Log(LogLevel level, string message)
    {
        bool locked = false;
        try
        {
            _lock.Enter(ref locked);
            
            _writer.WriteLine(_formatter.FormatLog(level, _category, message));
            _writer.Flush();
        }
        finally
        {
            if (locked) _lock.Exit();
        }
    }

    public void Log(LogLevel level, string format, params object?[] args)
    {
        Log(level, String.Format(format, args));
    }
}
public sealed class FileLoggingFactory : ILoggerFactory
{
    private readonly StreamWriter _writer;
    private readonly string _fileName;
    private readonly ILogFormatter _formatter;
    private bool _disposed = false;
    
    public FileLoggingFactory(string fileName, ILogFormatter formatter)
    {
        _fileName = $"{fileName}_{DateTime.UtcNow:yy_MM_dd-hh_mm_ss.fff}.log";

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var filePath = Path.Combine(baseDir, _fileName);
        string basePath = Path.GetDirectoryName(filePath)!;
            
        if (!Directory.Exists(basePath))
        {
            Directory.CreateDirectory(basePath);
        }

        var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        _writer = new StreamWriter(fileStream);
        _formatter = formatter;
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _writer.Dispose();
            _disposed = true;
        }
    }

    public ILogger CreateLogger(string category)
    {
        return new StreamLogger(_writer, category, _formatter);
    }
}

