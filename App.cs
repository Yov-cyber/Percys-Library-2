using System.Windows;
using ComicReader.Services;
using System.Globalization;
using System;
using System.Linq;
using System.Collections.Generic;
using ComicReader.Core.Services;
using ComicReader.ViewModels;
using ComicReader.Core.Adapters;
using ComicReader.Core.Abstractions;

namespace ComicReader
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // If invoked with --validate-themes, run a headless theme validation pass and exit
            try
            {
                if (e?.Args != null && e.Args.Any(a => a.Equals("--validate-themes", StringComparison.OrdinalIgnoreCase)))
                {
                    RunThemeValidationAndExit();
                    return;
                }
            }
            catch { }

            // Limpieza y validaciones antes de instanciar MainWindow
            try
            {
                DeduplicateMergedDictionaries();
                EnsureEssentialResources();
            }
            catch (Exception ex)
            {
                // No deberíamos bloquear el arranque por esta validación; loguear y seguir
                try { Logger.LogException("Error durante validación previa al arranque", ex); } catch { }
            }

            // Crear y mostrar MainWindow manualmente ahora que removimos StartupUri
            MainWindow main = null;
            try
            {
                main = new MainWindow();
                main.Show();
            }
            catch (Exception ex)
            {
                // Registrar excepción completa (incluyendo inner exceptions) para diagnóstico
                try
                {
                    Logger.LogException("Fallo al crear MainWindow", ex);
                    ComicReader.Utils.DevLogger.Error("Fallo al crear MainWindow: " + ex.ToString());
                }
                catch { }

                // Mostrar mensaje amigable al usuario con información mínima
                try { System.Windows.MessageBox.Show($"Error crítico al iniciar la interfaz: {ex.Message}\nRevisa el log para más detalles.", "Error crítico", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error); } catch { }

                // Re-lanzar para que el manejador global lo capture también
                throw;
            }

            try
            {
                // Si la app se invoca con un archivo asociado, abrirlo
                if (e?.Args != null && e.Args.Length > 0)
                {
                    var path = string.Join(" ", e.Args).Trim('"');
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        var mi = typeof(MainWindow).GetMethod("OpenComicFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        mi?.Invoke(main, new object[] { path });
                    }
                }
            }
            catch (Exception ex)
            {
                try { Logger.LogException("Error al abrir archivo por asociación", ex); } catch { }
            }

            // Abrir último cómic si el usuario lo solicita en la configuración
            try
            {
                if (SettingsManager.Settings != null && SettingsManager.Settings.OpenLastOnStartup && !string.IsNullOrWhiteSpace(SettingsManager.Settings.LastOpenedFilePath))
                {
                    var last = SettingsManager.Settings.LastOpenedFilePath;
                    if (System.IO.File.Exists(last) || System.IO.Directory.Exists(last))
                    {
                        var mi2 = typeof(MainWindow).GetMethod("OpenComicFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        mi2?.Invoke(main, new object[] { last });
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Remove duplicated ResourceDictionary entries (by Source) to avoid loading the same RD twice
        /// </summary>
        private static void DeduplicateMergedDictionaries()
        {
            try
            {
                var dicts = Application.Current.Resources.MergedDictionaries;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = dicts.Count - 1; i >= 0; i--)
                {
                    var rd = dicts[i];
                    var key = rd?.Source?.OriginalString;
                    if (string.IsNullOrWhiteSpace(key))
                        continue;
                    if (seen.Contains(key))
                    {
                        dicts.RemoveAt(i);
                        Logger.Log($"Removed duplicate ResourceDictionary: {key}", LogLevel.Info);
                    }
                    else
                    {
                        seen.Add(key);
                    }
                }
            }
            catch (Exception ex)
            {
                try { Logger.LogException("Error deduplicating merged dictionaries", ex); } catch { }
            }
        }

        /// <summary>
        /// Ensure some essential resource keys exist; if not, try to load ThemeTokens as fallback.
        /// This is defensive and will not throw; logs info for debugging.
        /// </summary>
        private static void EnsureEssentialResources()
        {
            try
            {
                var appRes = Application.Current.Resources;
                var required = new[] { "TextBrush", "PanelBackgroundBrush", "AccentBrush", "ReaderToggleButtonStyle" };
                bool missing = false;
                foreach (var k in required)
                {
                    if (!appRes.Contains(k))
                    {
                        Logger.Log($"Missing resource key: {k}", LogLevel.Warning);
                        missing = true;
                    }
                }
                if (missing)
                {
                    // Fallback: cargar Tokens.xaml + Components.xaml.
                    // ThemeTokens.xaml es ahora un stub vacio (Phase 1 movio
                    // todo a Tokens/Components). Cargar ese stub no resolveria
                    // los keys faltantes y solo enmascararia el problema con
                    // un log de "exito".
                    try
                    {
                        var tokensUri = new Uri("Themes/Tokens.xaml", UriKind.Relative);
                        var componentsUri = new Uri("Themes/Components.xaml", UriKind.Relative);
                        var tokensRd = new ResourceDictionary() { Source = tokensUri };
                        var componentsRd = new ResourceDictionary() { Source = componentsUri };
                        // Insert tokens first so Components puede resolver DynamicResources
                        Application.Current.Resources.MergedDictionaries.Insert(0, componentsRd);
                        Application.Current.Resources.MergedDictionaries.Insert(0, tokensRd);
                        Logger.Log("Loaded fallback Tokens.xaml + Components.xaml because required keys were missing.", LogLevel.Info);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException("Failed to load fallback Tokens.xaml/Components.xaml", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                try { Logger.LogException("Error ensuring essential resources", ex); } catch { }
            }
        }

        private void RunThemeValidationAndExit()
        {
            var results = new List<string>();
            try
            {
                var themes = ComicReader.Themes.ThemeManager.GetAvailableThemes();
                foreach (var t in themes)
                {
                    try
                    {
                        // Apply and give the dispatcher a moment
                        ComicReader.Themes.ThemeManager.CurrentTheme = t.Mode;
                        this.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        results.Add($"OK: {t.Name} ({t.Mode})");
                    }
                    catch (Exception ex)
                    {
                        results.Add($"ERROR: {t.Name} ({t.Mode}) -> {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                results.Add("Fatal error during theme validation: " + ex.Message);
            }

            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var dir = System.IO.Path.Combine(appData, "PercysLibrary", "validation");
                System.IO.Directory.CreateDirectory(dir);
                var outFile = System.IO.Path.Combine(dir, "theme-validation-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".log");
                System.IO.File.WriteAllLines(outFile, results);
                Logger.Log("Theme validation finished. Results written to " + outFile, LogLevel.Info);
            }
            catch { }

            // Show a simple message and exit
            try { MessageBox.Show("Theme validation completed. Revisa el log en %AppData%\\PercysLibrary\\validation.", "Validación de temas", MessageBoxButton.OK, MessageBoxImage.Information); } catch { }
            Environment.Exit(0);
        }
        public App()
        {
            try
            {
                ComicReader.Utils.DevLogger.Info("Iniciando aplicación...");
                
                // Inicializar sistemas de logging (Legacy + Moderno con Serilog)
                Logger.Initialize();
                ComicReader.Utils.ModernLogger.Initialize();
                ComicReader.Utils.DevLogger.Info("Logger inicializado correctamente");
                ComicReader.Utils.ModernLogger.Info("✓ Sistema de logging moderno activado");
                
                this.DispatcherUnhandledException += App_DispatcherUnhandledException;
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    try
                    {
                        var ex = e.ExceptionObject as Exception;
                        Logger.LogException("UnhandledException (AppDomain.CurrentDomain)", ex ?? new Exception("Unknown domain exception"));
                    }
                    catch { }
                };
                System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
                {
                    try
                    {
                        Logger.LogException("UnobservedTaskException (TaskScheduler)", e.Exception);
                        e.SetObserved();
                    }
                    catch { }
                };
                ComicReader.Utils.DevLogger.Info("Manejador de excepciones configurado");
                
                // ═══════════════════════════════════════════════════════════════
                // NUEVO SISTEMA DE PERSISTENCIA (ÚNICO Y DEFINITIVO)
                // ═══════════════════════════════════════════════════════════════
                ComicReader.Utils.ModernLogger.Info("═══════════════════════════════════");
                ComicReader.Utils.ModernLogger.Info("  INICIALIZANDO SISTEMA DE PERSISTENCIA V3.0");
                ComicReader.Utils.ModernLogger.Info("═══════════════════════════════════");
                
                // Inicializar ConfigurationManager (carga config + limpia archivos legacy)
                var initTask = ComicReader.Services.Persistence.ConfigurationManager.Instance.InitializeAsync();
                initTask.GetAwaiter().GetResult();
                ComicReader.Utils.ModernLogger.Info("✓ ConfigurationManager inicializado");
                
                // Inicializar PersistenceIntegrator (aplica configuración a todos los servicios)
                var integrationTask = ComicReader.Services.PersistenceIntegrator.Instance.InitializeApplicationAsync();
                integrationTask.GetAwaiter().GetResult();
                ComicReader.Utils.ModernLogger.Info("✓ PersistenceIntegrator inicializado");
                
                var stats = ComicReader.Services.PersistenceIntegrator.Instance.GetStatistics();
                ComicReader.Utils.ModernLogger.Info($"→ Total configuraciones guardadas: {stats.TotalSaves}");
                ComicReader.Utils.ModernLogger.Info($"→ Total configuraciones cargadas: {stats.TotalLoads}");
                ComicReader.Utils.ModernLogger.Info($"→ Backups creados: {stats.TotalBackups}");
                ComicReader.Utils.ModernLogger.Info($"→ Archivos legacy eliminados: {stats.LegacyFilesDeleted}");
                ComicReader.Utils.ModernLogger.Info("═══════════════════════════════════");
                
                // Cargar configuración legacy de SettingsManager (temporal, para compatibilidad)
                SettingsManager.LoadSettings();
                ComicReader.Utils.DevLogger.Info("Configuraciones legacy cargadas (compatibilidad temporal)");

                // Registro de servicios básicos (fase inicial DI ligera)
                // Registrar el loader progresivo por defecto para mejorar la experiencia de carga
                ServiceLocator.RegisterSingleton<IComicPageLoader>(new ComicReader.Services.ProgressivePageLoader());
                ServiceLocator.RegisterSingleton<IBookmarkService>(new BookmarkServiceAdapter());
                ServiceLocator.RegisterSingleton<ISettingsService>(new SettingsServiceAdapter());
                ServiceLocator.RegisterSingleton<ILogService>(new LogServiceAdapter());
                ServiceLocator.RegisterSingleton<IImageCache>(new MultiLevelImageCache());
                ServiceLocator.RegisterSingleton<ComicReader.Core.Abstractions.IReadingStatsService>(new ReadingStatsService());
                ComicReader.Utils.DevLogger.Info("Servicios registrados en ServiceLocator");
                // Register UndoService globally using the ToastService as the UI invoker
                try
                {
                    ServiceLocator.RegisterSingleton<ComicReader.Services.IUndoService>(new ComicReader.Services.UndoService(ComicReader.Services.ToastService.Show));
                }
                catch { }
                // Register a global ThumbnailManager and subscribe to settings changes so cache limits
                // and concurrency can be updated at runtime.
                try
                {
                    var concurrency = 2;
                    try { concurrency = SettingsManager.Settings?.ConcurrencyCap ?? concurrency; } catch { }
                    var maxFiles = 500;
                    try { maxFiles = SettingsManager.Settings?.ThumbCacheMaxFiles ?? maxFiles; } catch { }
                    var maxBytes = 200 * 1024 * 1024L;
                    try { maxBytes = SettingsManager.Settings?.ThumbCacheMaxBytes ?? maxBytes; } catch { }

                    var thumbMgr = new ComicReader.Services.ThumbnailManager(Math.Max(1, concurrency), Math.Max(1, maxFiles), Math.Max(1024, maxBytes));
                    ServiceLocator.RegisterSingleton<ComicReader.Services.ThumbnailManager>(thumbMgr);

                    // Listen to settings changes to reconfigure the ThumbnailManager at runtime
                    try
                    {
                        SettingsManager.Settings.PropertyChanged += (s, ev) =>
                        {
                            try
                            {
                                if (ev.PropertyName == nameof(Services.AppSettings.ThumbCacheMaxFiles)
                                    || ev.PropertyName == nameof(Services.AppSettings.ThumbCacheMaxBytes)
                                    || ev.PropertyName == nameof(Services.AppSettings.ConcurrencyCap))
                                {
                                    try
                                    {
                                        var c = SettingsManager.Settings.ConcurrencyCap;
                                        var mf = SettingsManager.Settings.ThumbCacheMaxFiles;
                                        var mb = SettingsManager.Settings.ThumbCacheMaxBytes;
                                        thumbMgr.Reconfigure(Math.Max(1, c), Math.Max(1, mf), Math.Max(1024, mb));
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                        };
                    }
                    catch { }
                }
                catch { }
                
                ComicReader.Utils.DevLogger.Info("Aplicación iniciada exitosamente");
            }
            catch (Exception ex)
            {
                ComicReader.Utils.DevLogger.Error($"Error crítico durante la inicialización: {ex.Message}");
                ComicReader.Utils.DevLogger.Error($"Stack trace: {ex.StackTrace}");
                
                try { Logger.LogException("Fatal init error", ex); } catch { }

                try
                {
                    MessageBox.Show($"Error crítico durante la inicialización:\n{ex.Message}\n\n{ex.StackTrace}", 
                                  "Error de Inicialización", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch 
                {
                    // Si MessageBox falla, al menos tenemos el output de consola
                }
                
                Environment.Exit(1);
            }
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                ComicReader.Utils.DevLogger.Error($"Excepción no controlada: {e.Exception.Message}");
                ComicReader.Utils.DevLogger.Error($"Stack trace: {e.Exception.StackTrace}");
                
                Logger.LogException("Unhandled exception caught by App_DispatcherUnhandledException.", e.Exception);
                MessageBox.Show($"Error inesperado:\n{e.Exception.Message}\n\n{e.Exception.StackTrace}", "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            }
            catch (Exception logEx)
            {
                ComicReader.Utils.DevLogger.Error($"Error en el manejador de excepciones: {logEx.Message}");
                e.Handled = true;
                Environment.Exit(1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                ComicReader.Utils.ModernLogger.Info("Cerrando Percy's Library...");
                // Forzar persistencia de ajustes antes de salir
                SettingsManager.SaveNow();
                try
                {
                    var cts = new System.Threading.CancellationTokenSource(1500);
                    SettingsManager.FlushPendingSavesAsync(cts.Token).GetAwaiter().GetResult();
                }
                catch { }
                // Cerrar sesión de lectura activa para que sus paginas
                // computen en stats y disparen logros pendientes.
                try
                {
                    var stats = ComicReader.Core.Services.ServiceLocator.TryGet<ComicReader.Core.Abstractions.IReadingStatsService>();
                    stats?.EndSession();
                    ComicReader.Services.AchievementService.Instance.Refresh();
                }
                catch { }
                ComicReader.Utils.ModernLogger.Shutdown();
            }
            catch { }
            base.OnExit(e);
        }

        // Shared ViewModel for the Favorites/Collections UI so controls and windows can bind to the same instance
        public CollectionViewModel FavoritesViewModel { get; } = new CollectionViewModel();

        public static void ApplyTheme(string themeName)
        {
            try
            {
                // 1) If the theme maps to a ThemeMode we manage programmatically, prefer that
                try
                {
                    if (!string.IsNullOrWhiteSpace(themeName) && Enum.TryParse<ComicReader.Services.ThemeMode>(themeName, true, out var tm))
                    {
                        ComicReader.Themes.ThemeManager.ApplyTheme(tm);
                        Logger.Log($"Applied programmatic theme: {themeName}", LogLevel.Info);
                        return;
                    }

                    // 1b) Try to match the human-friendly names from ThemeManager.GetAvailableThemes()
                    var avail = ComicReader.Themes.ThemeManager.GetAvailableThemes();
                    var match = avail.FirstOrDefault(t => string.Equals(t.Name, themeName, StringComparison.OrdinalIgnoreCase)
                                                       || t.Name.IndexOf(themeName ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (match != null)
                    {
                        ComicReader.Themes.ThemeManager.ApplyTheme(match.Mode);
                        Logger.Log($"Applied programmatic theme via ThemeInfo match: {match.Name} ({match.Mode})", LogLevel.Info);
                        return;
                    }
                }
                catch (Exception exEnum)
                {
                    // no crash here - we'll fall back to trying XAML files
                    Logger.LogException("Error while trying to map theme to ThemeMode", exEnum);
                }

                // 2) Remove previously loaded Theme resource dictionaries (heuristic)
                try
                {
                    var oldThemeDictionaries = Application.Current.Resources.MergedDictionaries
                        .Where(rd => rd.Source != null && rd.Source.OriginalString.IndexOf("theme", StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();

                    foreach (var rd in oldThemeDictionaries)
                    {
                        Application.Current.Resources.MergedDictionaries.Remove(rd);
                    }
                }
                catch { }

                // 3) Try a few filename normalizations to find an on-disk XAML theme in Themes/.
                bool loaded = false;
                var candidates = new List<string>();
                string raw = themeName ?? string.Empty;
                string sanitized = new string(raw.Normalize(System.Text.NormalizationForm.FormD)
                    .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    .ToArray()).Replace(" ", string.Empty);

                candidates.Add($"Themes/{raw}Theme.xaml");
                candidates.Add($"Themes/{sanitized}Theme.xaml");
                candidates.Add($"Themes/{raw}theme.xaml");
                candidates.Add($"Themes/{sanitized}theme.xaml");
                candidates.Add($"Themes/{raw}.xaml");
                candidates.Add($"Themes/{sanitized}.xaml");

                // Also try to match any file in the Themes directory that contains the name (case-insensitive)
                try
                {
                    var themesDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Themes");
                    if (System.IO.Directory.Exists(themesDir))
                    {
                        var files = System.IO.Directory.GetFiles(themesDir, "*.xaml", System.IO.SearchOption.TopDirectoryOnly);
                        foreach (var f in files)
                        {
                            var filename = System.IO.Path.GetFileNameWithoutExtension(f);
                            if (!candidates.Any(c => c.EndsWith(filename + ".xaml", StringComparison.OrdinalIgnoreCase)))
                            {
                                if (filename.IndexOf(raw ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    filename.IndexOf(sanitized ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    candidates.Add("Themes/" + filename + ".xaml");
                                }
                            }
                        }
                    }
                }
                catch { }

                foreach (var cand in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var themeUri = new Uri(cand, UriKind.Relative);
                        var themeRd = new ResourceDictionary() { Source = themeUri };
                        Application.Current.Resources.MergedDictionaries.Add(themeRd);
                        Logger.Log($"Applied theme XAML: {cand}", LogLevel.Info);
                        loaded = true;
                        break;
                    }
                    catch (Exception exCandidate)
                    {
                        // ignore candidate-specific failures and try next
                        Logger.LogException($"Failed to load theme candidate: {cand}", exCandidate);
                    }
                }

                if (!loaded)
                {
                    Logger.Log($"No matching theme found for '{themeName}', falling back to programmatic Dark theme.", LogLevel.Warning);
                    ComicReader.Themes.ThemeManager.ApplyTheme(ComicReader.Services.ThemeMode.Dark);
                    Logger.Log("Applied fallback programmatic theme: Dark", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException($"Unexpected error while applying theme: {themeName}", ex);
                try
                {
                    ComicReader.Themes.ThemeManager.ApplyTheme(ComicReader.Services.ThemeMode.Dark);
                    Logger.Log("Applied fallback programmatic theme: Dark after error.", LogLevel.Info);
                }
                catch (Exception fallbackEx)
                {
                    Logger.LogException("Failed to apply fallback Dark theme after unexpected error.", fallbackEx);
                }
            }
        }
    }
}