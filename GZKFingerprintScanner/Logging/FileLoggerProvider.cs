using System;
using System.IO;
using System.Threading;
using System.Collections.Concurrent;

namespace GZKFingerprintScanner.Logging
{
    public class FileLoggerProvider : IDisposable
    {
        private readonly string _basePath;
        private readonly object _gate = new object();
        private readonly ConcurrentDictionary<string, FileLogger> _loggers = new ConcurrentDictionary<string, FileLogger>();
        private readonly LogLevel _minLevel;
        private string _currentFilePath;
        private DateTime _currentDate;

        public FileLoggerProvider(string basePath, LogLevel minLevel = LogLevel.Information)
        {
            _basePath = basePath;
            _minLevel = minLevel;
            _currentDate = DateTime.Now;
            _currentFilePath = GetLogFilePath(_currentDate);
            EnsureDirectoryExists();
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, name => new FileLogger(this, name, _minLevel));
        }

        public void WriteLine(string line)
        {
            lock (_gate)
            {
                DateTime now = DateTime.Now;
                if (now.Date != _currentDate.Date)
                {
                    _currentDate = now;
                    _currentFilePath = GetLogFilePath(_currentDate);
                    EnsureDirectoryExists();
                }

                try
                {
                    File.AppendAllText(_currentFilePath, line + Environment.NewLine);
                }
                catch
                {
                    // Swallow logging errors to prevent crashes
                }
            }
        }

        public void Dispose()
        {
            _loggers.Clear();
        }

        private void EnsureDirectoryExists()
        {
            try
            {
                string dir = Path.GetDirectoryName(_basePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch
            {
                // Swallow
            }
        }

        private string GetLogFilePath(DateTime date)
        {
            string dir = Path.GetDirectoryName(_basePath);
            string fileName = string.Format("app-{0:yyyyMMdd}.log", date);
            return Path.Combine(dir ?? string.Empty, fileName);
        }
    }
}
