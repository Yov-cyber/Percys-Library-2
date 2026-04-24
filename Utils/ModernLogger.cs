using System;
using System.IO;
using Serilog;
using Serilog.Events;

namespace ComicReader.Utils
{
    /// <summary>
    /// Sistema de logging moderno usando Serilog con rotación automática de archivos
    /// </summary>
    public static class ModernLogger
    {
        private static bool _isInitialized = false;

        public static void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                var logsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PercysLibrary",
                    "logs"
                );

                Directory.CreateDirectory(logsPath);

                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("Application", "Percy's Library")
                    .WriteTo.Console(
                        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
                    )
                    .WriteTo.File(
                        path: Path.Combine(logsPath, "percys-library-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 7,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                        fileSizeLimitBytes: 10 * 1024 * 1024, // 10 MB
                        rollOnFileSizeLimit: true
                    )
                    .CreateLogger();

                _isInitialized = true;
                Log.Information("═══════════════════════════════════════");
                Log.Information("  Percy's Library - Sistema Moderno");
                Log.Information("  Logging inicializado con Serilog");
                Log.Information("═══════════════════════════════════════");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error inicializando ModernLogger: {ex.Message}");
            }
        }

        public static void Shutdown()
        {
            try
            {
                Log.Information("Cerrando sistema de logging...");
                Log.CloseAndFlush();
            }
            catch { }
        }

        // Métodos convenientes
        public static void Info(string message) => Log.Information(message);
        public static void Debug(string message) => Log.Debug(message);
        public static void Warning(string message) => Log.Warning(message);
        public static void Error(string message) => Log.Error(message);
        public static void Error(Exception ex, string message) => Log.Error(ex, message);
        public static void Fatal(string message) => Log.Fatal(message);
        public static void Fatal(Exception ex, string message) => Log.Fatal(ex, message);
    }
}
