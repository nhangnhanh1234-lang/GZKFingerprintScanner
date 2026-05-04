using System;
using System.IO;
using System.Threading;

namespace GZKFingerprintScanner.Logging
{
    public class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly FileLoggerProvider _provider;
        private readonly LogLevel _minLevel;

        public FileLogger(FileLoggerProvider provider, string categoryName, LogLevel minLevel = LogLevel.Information)
        {
            _provider = provider;
            _categoryName = categoryName;
            _minLevel = minLevel;
        }

        public void Log(LogLevel level, string message)
        {
            if (level < _minLevel) return;
            _provider.WriteLine(FormatMessage(level, message));
        }

        public void Log(LogLevel level, string message, Exception exception)
        {
            if (level < _minLevel) return;
            string formatted = FormatMessage(level, message);
            if (exception != null)
                formatted += Environment.NewLine + exception.ToString();
            _provider.WriteLine(formatted);
        }

        public void LogDebug(string message)
        {
            Log(LogLevel.Debug, message);
        }

        public void LogInformation(string message)
        {
            Log(LogLevel.Information, message);
        }

        public void LogWarning(string message)
        {
            Log(LogLevel.Warning, message);
        }

        public void LogError(string message)
        {
            Log(LogLevel.Error, message);
        }

        public void LogError(Exception exception, string message)
        {
            Log(LogLevel.Error, message, exception);
        }

        private string FormatMessage(LogLevel level, string message)
        {
            string levelStr = level.ToString().ToUpper().Substring(0, 4);
            return string.Format("[{0:HH:mm:ss.fff}] [{1,-4}] {2}  {3}",
                DateTime.Now, levelStr, _categoryName, message);
        }
    }
}
