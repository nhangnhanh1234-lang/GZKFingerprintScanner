using System;

namespace GZKFingerprintScanner.Logging
{
    public enum LogLevel
    {
        Debug = 0,
        Information = 1,
        Warning = 2,
        Error = 3,
        Critical = 4
    }

    public interface ILogger
    {
        void Log(LogLevel level, string message);
        void Log(LogLevel level, string message, Exception exception);
        void LogDebug(string message);
        void LogInformation(string message);
        void LogWarning(string message);
        void LogError(string message);
        void LogError(Exception exception, string message);
    }
}
