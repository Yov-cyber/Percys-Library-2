using System;
using System.Diagnostics;
using ComicReader.Services;

namespace ComicReader.Utils
{
    public static class DevLogger
    {
        // Escribe debug solo si el debugger está adjunto o si la setting EnableDeveloperLogs está activa
        public static void Debug(string line)
        {
            try
            {
                bool enabled = Debugger.IsAttached;
                try { enabled = enabled || (SettingsManager.Settings?.EnableDeveloperLogs == true); } catch { }
                if (!enabled) return;
                // Formato consistente con timestamp
                Console.WriteLine($"[DEBUG] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} {line}");
            }
            catch { }
        }

        public static void Info(string line)
        {
            try { Console.WriteLine($"[Info] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} {line}"); } catch { }
        }

        public static void Error(string line)
        {
            try { Console.WriteLine($"[Error] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} {line}"); } catch { }
        }
    }
}
