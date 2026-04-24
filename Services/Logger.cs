using System;
using System.IO;
using ComicReader.Core.Abstractions;

namespace ComicReader.Services
{
    public static class Logger
    {
        private static readonly object _sync = new object();
        private static string _appDataLogPath;
        private static string _localLogPath;

        public static void Initialize()
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PercysLibrary", "Logs");
                Directory.CreateDirectory(appDataDir);
                _appDataLogPath = Path.Combine(appDataDir, $"app-{timestamp}.log");

                var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory;
                _localLogPath = Path.Combine(baseDir, "app.log");

                Log("Logger inicializado", LogLevel.Info);
            }
            catch
            {
                // Ignorar errores del logger para no bloquear la app
            }
        }

        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
            try
            {
                Console.WriteLine(line);
                lock (_sync)
                {
                    if (!string.IsNullOrEmpty(_appDataLogPath)) File.AppendAllText(_appDataLogPath, line + Environment.NewLine);
                    if (!string.IsNullOrEmpty(_localLogPath)) File.AppendAllText(_localLogPath, line + Environment.NewLine);
                }
            }
            catch { /* no-op */ }
        }

        public static void LogException(string message, Exception ex)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Error] {message}: {ex?.Message}\n{ex?.StackTrace}";
            try
            {
                Console.WriteLine(line);
                lock (_sync)
                {
                    if (!string.IsNullOrEmpty(_appDataLogPath)) File.AppendAllText(_appDataLogPath, line + Environment.NewLine);
                    if (!string.IsNullOrEmpty(_localLogPath)) File.AppendAllText(_localLogPath, line + Environment.NewLine);
                }
            }
            catch { /* no-op */ }
        }
    }
}
