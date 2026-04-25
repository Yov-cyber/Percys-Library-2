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
                        // Migracion Phase 3: la barra del lector paso de tener
                        // su propia fila (Grid.Row=1, Height=Auto) a ser un
                        // overlay sobre el contenido (Grid.Row=2, Top, ZIndex).
                        // Antes el default de HideOverlayOnlyInImmersive era
                        // true, y bajo el layout viejo eso significaba 'la
                        // barra ocupa su fila siempre en modo ventana'. Con el
                        // layout nuevo eso significaria 'la barra obstruye
                        // permanentemente el comic en modo ventana sin forma de
                        // descartarla'. Para usuarios pre-Phase 3 forzamos el
                        // valor a false una sola vez para que el auto-hide
                        // funcione en modo ventana. Si el usuario lo quiere
                        // siempre visible, puede re-activarlo desde Settings.
                        if (!obj.OverlayLayoutMigrationApplied)
                        {
                            obj.HideOverlayOnlyInImmersive = false;
                            obj.OverlayLayoutMigrationApplied = true;
                        }
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
            // Notificar a consumidores externos que la instancia cambio. Antes,
            // suscriptores como HomeView (greeting) o CollectionsView que se
            // suscribian a Settings.PropertyChanged quedaban con un handle al
            // objeto viejo despues de un import/reset. Disparamos el evento
            // simulando un PropertyChanged "all properties" (PropertyName=null)
            // y ademas SettingsReplaced para que el suscriptor pueda re-attach
            // si quiere escuchar cambios futuros.
            try { SettingsReplaced?.Invoke(null, EventArgs.Empty); } catch { }
            try { SettingChanged?.Invoke(null, new PropertyChangedEventArgs(null)); } catch { }
        }

        /// <summary>
        /// Evento que se dispara cuando cambia cualquier propiedad del Settings actual,
        /// incluso si la instancia subyacente fue reemplazada via ReplaceSettings().
        /// Codigo que necesite reaccionar a cambios de Settings (UI binding manual,
        /// caches dependientes, etc.) debe suscribirse a este evento en vez de
        /// SettingsManager.Settings.PropertyChanged, porque la instancia de Settings
        /// puede cambiar en cualquier momento (import, reset).
        /// </summary>
        public static event PropertyChangedEventHandler SettingChanged;

        /// <summary>
        /// Evento que se dispara cuando ReplaceSettings() crea una instancia nueva.
        /// Util si el suscriptor mantiene su propio handler con la instancia vieja
        /// y necesita liberar/reattach explicitamente.
        /// </summary>
        public static event EventHandler SettingsReplaced;

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
            // Schedule a save when a setting changes, honoring the AutoSaveEnabled flag.
            // El forward al evento estatico debe correr SIEMPRE, incluso cuando
            // AutoSaveEnabled==false (ej. el usuario desactivo persistencia pero
            // cambia su nombre y queremos que el saludo del HomeView se refresque).
            try
            {
                bool autoSave = true;
                try { autoSave = Settings == null || Settings.AutoSaveEnabled != false; }
                catch { }
                if (autoSave) SaveSettings();
            }
            catch { }
            // Forward al evento estatico para suscriptores que viven mas alla
            // que una instancia particular de Settings.
            try { SettingChanged?.Invoke(sender, e); } catch { }
        }
    }
}
