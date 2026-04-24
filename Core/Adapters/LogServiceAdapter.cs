using System;
using ComicReader.Core.Abstractions;
using ComicReader.Services;

namespace ComicReader.Core.Adapters
{
    public class LogServiceAdapter : ILogService
    {
        public void Log(string message, LogLevel level = LogLevel.Info) => Logger.Log(message, level);
        public void LogException(string message, Exception ex) => Logger.LogException(message, ex);
    }
}
