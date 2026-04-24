using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.ComponentModel;

namespace ComicReader.Services
{
    /// <summary>
    /// Improved SettingsManager: persists settings to %LocalAppData%\PercysLibrary\settings.json,
    /// supports debounced saves, SaveNow and FlushPendingSavesAsync, and subscribes to
    /// AppSettings.PropertyChanged to autoschedule saves.
    /// This implementation is intentionally conservative and safe for development.
    /// </summary>
    public static class SettingsManager
    {
        private static readonly object _sync = new object();
        private static CancellationTokenSource _ctsDebounce;
        private static Task _pendingSaveTask;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        // Public settings instance (many callers expect to mutate this instance directly)
        public static AppSettings Settings { get; set; } = new AppSettings();

        // Debounce delay in milliseconds for SaveSettings
        private const int SaveDebounceMs = 500;

        static SettingsManager()
        {
            // Subscribe to property changes on the default Settings instance
            TrySubscribe(Settings);
        }

        public static void LoadSettings()
        {
            try
            {
                var path = GetSettingsFilePath();
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    var txt = File.ReadAllText(path);
                    var obj = JsonSerializer.Deserialize<AppSettings>(txt, _jsonOptions);
                    if (obj != null)
                    {
                        ReplaceSettings(obj);
                    }
                }
                // No per-screen HomeView theme persisted by default (feature removed)
            }
            catch { /* swallow to keep load best-effort */ }
        }

        /// <summary>
        /// Schedule a debounced save of the current Settings.
        /// This method is non-blocking.
        /// </summary>
        public static void SaveSettings()
        {
            lock (_sync)
            {
                // Cancel any previous debounce timer
                try { _ctsDebounce?.Cancel(); } catch { }
                _ctsDebounce = new CancellationTokenSource();
                var token = _ctsDebounce.Token;

                // Start a background task that waits the debounce period then writes
                _pendingSaveTask = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(SaveDebounceMs, token).ConfigureAwait(false);
                        await WriteToFileAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { /* canceled - ignore */ }
                    catch { /* swallow */ }
                }, token);
            }
        }

        /// <summary>
        /// Flush any pending saves and write immediately.
        /// </summary>
        public static void SaveNow()
        {
            Task pending = null;
            lock (_sync)
            {
                try { _ctsDebounce?.Cancel(); } catch { }
                pending = _pendingSaveTask;
                _pendingSaveTask = null;
            }

            try { pending?.Wait(2000); } catch { }

            try { WriteToFileAsync().GetAwaiter().GetResult(); } catch { }
        }

        public static void ResetToDefaults()
        {
            ReplaceSettings(new AppSettings());
        }

        /// <summary>
        /// Wait for any pending debounced save to finish (or until token canceled).
        /// </summary>
        public static async Task FlushPendingSavesAsync(CancellationToken token)
        {
            Task pending = null;
            lock (_sync)
            {
                pending = _pendingSaveTask;
            }
            if (pending == null) return;
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    var t = await Task.WhenAny(pending, Task.Delay(Timeout.Infinite, linked.Token)).ConfigureAwait(false);
                    if (t == pending) await pending.ConfigureAwait(false);
                }
            }
            catch { }
        }

        public static void ReplaceSettings(AppSettings newSettings)
        {
            if (newSettings == null) return;
            // Unsubscribe previous
            TryUnsubscribe(Settings);
            Settings = newSettings;
            TrySubscribe(Settings);
            // Persist immediately to ensure new shape is saved
            SaveSettings();
        }

        private static async Task WriteToFileAsync()
        {
            try
            {
                var path = GetSettingsFilePath();
                if (string.IsNullOrWhiteSpace(path))
                {
                    ComicReader.Utils.DevLogger.Error("WriteToFileAsync: Path is null or empty");
                    return;
                }
                
                var dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                    ComicReader.Utils.DevLogger.Info($"Directorio creado: {dir}");
                }
                
                var txt = JsonSerializer.Serialize(Settings, _jsonOptions);
                ComicReader.Utils.DevLogger.Info($"JSON serializado, longitud: {txt.Length} caracteres");
                
                // Write atomically: write to temp then move
                var tmp = path + ".tmp";
                await File.WriteAllTextAsync(tmp, txt).ConfigureAwait(false);
                ComicReader.Utils.DevLogger.Info($"Archivo temporal escrito: {tmp}");
                
                try 
                { 
                    File.Replace(tmp, path, null);
                    ComicReader.Utils.DevLogger.Info($"✓ Archivo reemplazado exitosamente: {path}");
                }
                catch (Exception replaceEx)
                {
                    ComicReader.Utils.DevLogger.Info($"⚠ Replace falló, intentando Delete+Move: {replaceEx.Message}");
                    try { File.Delete(path); } catch { }
                    try 
                    { 
                        File.Move(tmp, path);
                        ComicReader.Utils.DevLogger.Info($"✓ Archivo movido exitosamente: {path}");
                    } 
                    catch (Exception moveEx)
                    {
                        ComicReader.Utils.DevLogger.Error($"✗ Move también falló: {moveEx.Message}");
                    }
                }
                
                // Verificar que el archivo se escribió correctamente
                if (File.Exists(path))
                {
                    var verifyContent = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                    if (verifyContent.Length > 0)
                    {
                        ComicReader.Utils.DevLogger.Info($"✓✓ Archivo verificado, tamaño: {verifyContent.Length} caracteres");
                    }
                    else
                    {
                        ComicReader.Utils.DevLogger.Error("✗ Archivo existe pero está vacío");
                    }
                }
                else
                {
                    ComicReader.Utils.DevLogger.Error($"✗ Archivo NO existe después de escribir: {path}");
                }
            }
            catch (Exception ex)
            {
                ComicReader.Utils.DevLogger.Error($"✗✗✗ ERROR en WriteToFileAsync: {ex.Message}");
                ComicReader.Utils.DevLogger.Error($"Stack: {ex.StackTrace}");
            }
        }

        public static string GetSettingsFilePath()
        {
            try
            {
                var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appDir = Path.Combine(baseDir ?? string.Empty, "PercysLibrary");
                try { Directory.CreateDirectory(appDir); } catch { }
                return Path.Combine(appDir, "settings.json");
            }
            catch { return null; }
        }

        public static string GetLogsFolderPath()
        {
            try
            {
                var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var logs = Path.Combine(baseDir ?? string.Empty, "PercysLibrary", "logs");
                try { Directory.CreateDirectory(logs); } catch { }
                return logs;
            }
            catch { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        private static void TrySubscribe(object obj)
        {
            if (obj is INotifyPropertyChanged npc)
            {
                try { npc.PropertyChanged += Settings_PropertyChanged; } catch { }
            }
        }

        private static void TryUnsubscribe(object obj)
        {
            if (obj is INotifyPropertyChanged npc)
            {
                try { npc.PropertyChanged -= Settings_PropertyChanged; } catch { }
            }
        }

        private static void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Schedule a save when a setting changes, honoring the AutoSaveEnabled flag when present
            try
            {
                try
                {
                    if (Settings != null && Settings.AutoSaveEnabled == false)
                    {
                        // respect user's choice to disable auto-save
                        return;
                    }
                }
                catch { }
                SaveSettings();
            }
            catch { }
        }
    }
}
