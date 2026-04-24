// FileName: Services/ErrorHandling/ErrorHandler.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using ComicReader.Services.Notifications;

namespace ComicReader.Services.ErrorHandling
{
    /// <summary>
    /// Sistema centralizado de manejo de errores con logging, recovery y mensajes user-friendly
    /// </summary>
    public class ErrorHandler
    {
        private static ErrorHandler _instance;
        private readonly object _lock = new object();
        private readonly List<ErrorLog> _errorHistory = new List<ErrorLog>();
        private const int MaxErrorHistory = 100;

        public static ErrorHandler Instance => _instance ??= new ErrorHandler();

        private ErrorHandler() { }

        #region Public API

        /// <summary>
        /// Maneja una excepción con contexto y recovery automático
        /// </summary>
        public void HandleException(Exception exception, string context = "", ErrorRecoveryStrategy strategy = ErrorRecoveryStrategy.Notify)
        {
            if (exception == null) return;

            try
            {
                var error = new ErrorLog
                {
                    Exception = exception,
                    Context = context,
                    Timestamp = DateTime.Now,
                    StackTrace = exception.StackTrace
                };

                lock (_lock)
                {
                    _errorHistory.Add(error);
                    if (_errorHistory.Count > MaxErrorHistory)
                        _errorHistory.RemoveAt(0);
                }

                // Logging
                LogError(error);

                // Recovery según estrategia
                switch (strategy)
                {
                    case ErrorRecoveryStrategy.Silent:
                        // Solo log, no notificar usuario
                        break;

                    case ErrorRecoveryStrategy.Notify:
                        NotifyUser(error);
                        break;

                    case ErrorRecoveryStrategy.NotifyAndRetry:
                        NotifyUser(error);
                        // TODO: Implementar retry logic
                        break;

                    case ErrorRecoveryStrategy.Critical:
                        HandleCriticalError(error);
                        break;
                }
            }
            catch (Exception handlingException)
            {
                // Error al manejar error - último recurso
                Debug.WriteLine($"Error en ErrorHandler: {handlingException.Message}");
                try
                {
                    MessageBox.Show($"Error crítico del sistema:\n{exception.Message}", 
                                  "Error Fatal", 
                                  MessageBoxButton.OK, 
                                  MessageBoxImage.Error);
                }
                catch { }
            }
        }

        /// <summary>
        /// Maneja un error esperado con mensaje personalizado
        /// </summary>
        public void HandleError(string message, string title = "Error", ErrorSeverity severity = ErrorSeverity.Error)
        {
            var error = new ErrorLog
            {
                Message = message,
                Context = title,
                Timestamp = DateTime.Now,
                Severity = severity
            };

            lock (_lock)
            {
                _errorHistory.Add(error);
                if (_errorHistory.Count > MaxErrorHistory)
                    _errorHistory.RemoveAt(0);
            }

            LogError(error);
            NotifyUser(error);
        }

        /// <summary>
        /// Obtiene el historial de errores recientes
        /// </summary>
        public IReadOnlyList<ErrorLog> GetErrorHistory()
        {
            lock (_lock)
            {
                return _errorHistory.ToArray();
            }
        }

        /// <summary>
        /// Exporta el log de errores a un archivo
        /// </summary>
        public string ExportErrorLog()
        {
            try
            {
                var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                                        "PercysLibrary", "ErrorLogs");
                Directory.CreateDirectory(logDir);

                var filename = $"ErrorLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                var filepath = Path.Combine(logDir, filename);

                var sb = new StringBuilder();
                sb.AppendLine("=".PadRight(80, '='));
                sb.AppendLine("PERCY'S LIBRARY - ERROR LOG");
                sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine("=".PadRight(80, '='));
                sb.AppendLine();

                lock (_lock)
                {
                    foreach (var error in _errorHistory)
                    {
                        sb.AppendLine($"[{error.Timestamp:HH:mm:ss}] {error.Severity}");
                        sb.AppendLine($"Context: {error.Context}");
                        sb.AppendLine($"Message: {error.Message ?? error.Exception?.Message}");
                        
                        if (error.Exception != null)
                        {
                            sb.AppendLine($"Exception: {error.Exception.GetType().Name}");
                            sb.AppendLine($"Stack Trace:\n{error.StackTrace}");
                        }
                        
                        sb.AppendLine("-".PadRight(80, '-'));
                        sb.AppendLine();
                    }
                }

                File.WriteAllText(filepath, sb.ToString());
                return filepath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al exportar log: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Limpia el historial de errores
        /// </summary>
        public void ClearHistory()
        {
            lock (_lock)
            {
                _errorHistory.Clear();
            }
        }

        #endregion

        #region Private Methods

        private void LogError(ErrorLog error)
        {
            try
            {
                var logMessage = $"[{error.Timestamp:yyyy-MM-dd HH:mm:ss}] {error.Severity} - {error.Context}: {error.Message ?? error.Exception?.Message}";
                
                // Log a consola en debug
                Debug.WriteLine(logMessage);
                
                // Log a archivo si existe Logger
                try
                {
                    ComicReader.Services.Logger.LogException(error.Context, error.Exception ?? new Exception(error.Message));
                }
                catch { }
            }
            catch { }
        }

        private void NotifyUser(ErrorLog error)
        {
            try
            {
                var message = GetUserFriendlyMessage(error);
                var title = error.Context ?? "Error";

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    switch (error.Severity)
                    {
                        case ErrorSeverity.Info:
                            NotificationService.Instance.Info(message, title);
                            break;
                        case ErrorSeverity.Warning:
                            NotificationService.Instance.Warning(message, title);
                            break;
                        case ErrorSeverity.Error:
                            NotificationService.Instance.Error(message, title, 7000);
                            break;
                        case ErrorSeverity.Critical:
                            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
                            break;
                    }
                });
            }
            catch { }
        }

        private void HandleCriticalError(ErrorLog error)
        {
            try
            {
                var logPath = ExportErrorLog();
                
                var message = $"Ha ocurrido un error crítico:\n\n{GetUserFriendlyMessage(error)}\n\n" +
                            $"Se ha guardado un registro detallado en:\n{logPath}\n\n" +
                            $"¿Deseas continuar o cerrar la aplicación?";

                var result = MessageBox.Show(message, 
                                           "Error Crítico", 
                                           MessageBoxButton.YesNo, 
                                           MessageBoxImage.Error);

                if (result == MessageBoxResult.No)
                {
                    Application.Current?.Shutdown();
                }
            }
            catch
            {
                Application.Current?.Shutdown();
            }
        }

        private string GetUserFriendlyMessage(ErrorLog error)
        {
            if (!string.IsNullOrEmpty(error.Message))
                return error.Message;

            if (error.Exception == null)
                return "Ha ocurrido un error desconocido";

            // Mensajes amigables según tipo de excepción
            return error.Exception switch
            {
                FileNotFoundException _ => "No se pudo encontrar el archivo especificado",
                DirectoryNotFoundException _ => "No se pudo encontrar el directorio especificado",
                UnauthorizedAccessException _ => "No tienes permisos para realizar esta operación",
                IOException _ => "Error al leer o escribir el archivo",
                OutOfMemoryException _ => "No hay suficiente memoria disponible. Intenta cerrar otros programas",
                InvalidOperationException _ => "La operación no es válida en este momento",
                ArgumentException _ => "Los datos proporcionados no son válidos",
                NotSupportedException _ => "Esta operación no está soportada",
                _ => $"Error: {error.Exception.Message}"
            };
        }

        #endregion
    }

    #region Supporting Classes

    public class ErrorLog
    {
        public DateTime Timestamp { get; set; }
        public Exception Exception { get; set; }
        public string Message { get; set; }
        public string Context { get; set; }
        public string StackTrace { get; set; }
        public ErrorSeverity Severity { get; set; } = ErrorSeverity.Error;
    }

    public enum ErrorSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    public enum ErrorRecoveryStrategy
    {
        /// <summary>Solo log, sin notificar al usuario</summary>
        Silent,
        
        /// <summary>Notificar al usuario con toast</summary>
        Notify,
        
        /// <summary>Notificar e intentar reintentar la operación</summary>
        NotifyAndRetry,
        
        /// <summary>Error crítico, notificar y posiblemente cerrar app</summary>
        Critical
    }

    #endregion
}
