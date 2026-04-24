using System;

namespace ComicReader.Core.Abstractions
{
    public interface ILogService
    {
        void Log(string message, LogLevel level = LogLevel.Info);
        void LogException(string message, Exception ex);
    }

    public enum LogLevel { Info, Warning, Error }
}
