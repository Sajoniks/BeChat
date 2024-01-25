using System.Text;
using BeChat.Logging.Loggers;

namespace BeChat.Logging;

public class LogFormatter : ILogFormatter
{
    private char[] _stringBuffer = new char[1024];
    private static SpinLock _lock = new();
    
    public string FormatLog(LogLevel level, string category, string message)
    {
        bool locked = false;
        try
        {
            _lock.Enter(ref locked);
            
            _stringBuffer[0] = '[';
            string date = DateTime.UtcNow.ToString("yy-MM-dd HH:mm:ss.ff");
            date.CopyTo(0, _stringBuffer, 1, date.Length);
            _stringBuffer[date.Length] = ']';

            int writePos = date.Length + 1 + Math.Max(0, 22 - date.Length);
            _stringBuffer[writePos++] = '[';

            string levelString = level.ToString();
            levelString.CopyTo(0, _stringBuffer, writePos, levelString.Length);
            writePos += levelString.Length;

            _stringBuffer[writePos++] = ']';

            int len = Math.Min(message.Length, _stringBuffer.Length - writePos);
            message.CopyTo(0, _stringBuffer, writePos, len);
            writePos += len;

            return new string(_stringBuffer.AsSpan(0, writePos));
        }
        finally
        {
            if (locked) _lock.Exit();
        }
    }
}