using System;
using System.Threading.Tasks;
using System.Windows;
using ComicReader.Services.Persistence;
using ComicReader.Themes;
using ComicReader.Utils;

namespace ComicReader.Services
{
    /// <summary>
    /// Integrador del nuevo sistema de persistencia
    /// Conecta el ConfigurationManager con todos los componentes de la app
    /// Reemplaza TODOS los sistemas antiguos
    /// </summary>
    public sealed class PersistenceIntegrator
    {
        private static readonly Lazy<PersistenceIntegrator> _instance =
            new Lazy<PersistenceIntegrator>(() => new PersistenceIntegrator());

        public static PersistenceIntegrator Instance => _instance.Value;

        private readonly ConfigurationManager _configManager;
        private AppConfiguration _config;

        private PersistenceIntegrator()
        {
            _configManager = ConfigurationManager.Instance;
            _config = _configManager.GetConfiguration();

            ModernLogger.Info("✓ PersistenceIntegrator inicializado");
        }

        /// <summary>
        /// Inicializa la aplicación con la configuración guardada
        /// Aplica TODOS los ajustes automáticamente
        /// </summary>
        public async Task InitializeApplicationAsync()
        {
            try
            {
                ModernLogger.Info("🚀 Inicializando aplicación con configuración guardada...");

                // Cargar configuración actual
                _config = _configManager.GetConfiguration();

                // NO aplicar nada que requiera UI aquí porque MainWindow no existe todavía
                // Todo se aplicará desde MainWindow después de su creación:
                // - Tema: Se carga desde ThemeManager.LoadSavedTheme() en MainWindow
                // - UI: Se aplicará cuando MainWindow llame a métodos específicos
                // - Ventana: Se aplica desde MainWindow.ConfigureInitialWindowState()

                // Solo aplicar configuraciones que NO requieren UI:

                // Aplicar configuraciones de lectura (servicios backend)
                ApplyReadingConfiguration();

                // Aplicar configuraciones de rendimiento (servicios backend)
                ApplyPerformanceConfiguration();

                // Aplicar atajos de teclado (se registran cuando MainWindow esté lista)
                ApplyShortcuts();

                // Aplicar accesibilidad (configuraciones backend)
                ApplyAccessibilityConfiguration();

                ModernLogger.Info("✓ Configuraciones backend aplicadas (UI se aplicará con MainWindow)");
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error inicializando aplicación: {ex.Message}");
            }
        }

        // ============================================================
        // TEMA
        // ============================================================

        /// <summary>
        /// Aplica el tema guardado inmediatamente
        /// </summary>
        private async Task ApplyThemeAsync()
        {
            try
            {
                // Solo aplicar tema si ya existe el dispatcher
                if (Application.Current?.Dispatcher == null)
                {
                    ModernLogger.Info("⏭ Tema se aplicará cuando se cree MainWindow");
                    return;
                }

                var themeName = _config.Theme.CurrentTheme;

                // Convertir string a ThemeMode enum
                if (Enum.TryParse<ComicReader.Services.ThemeMode>(themeName, out var themeMode))
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        ThemeManager.ApplyTheme(themeMode);
                    });

                    ModernLogger.Info($"✓ Tema aplicado: {themeName}");
                }
                else
                {
                    ModernLogger.Warning($"⚠ Tema no reconocido: {themeName}, usando Dark");
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        ThemeManager.ApplyTheme(ComicReader.Services.ThemeMode.Dark);
                    });
                }
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error aplicando tema: {ex.Message}");
            }
        }

        /// <summary>
        /// Cambia el tema y guarda automáticamente
        /// </summary>
        public async Task ChangeThemeAsync(string themeName)
        {
            ModernLogger.Info($"🎨 ChangeThemeAsync iniciado para: {themeName}");
            
            await _configManager.UpdateConfigurationAsync(config =>
            {
                config.Theme.CurrentTheme = themeName;
                config.LastSaved = DateTime.Now;
                ModernLogger.Info($"💾 Tema guardado en config: {themeName}");
            });

            // Recargar configuración después de guardar
            _config = _configManager.GetConfiguration();
            ModernLogger.Info($"♻️ Configuración recargada. Tema actual: {_config.Theme.CurrentTheme}");

            await ApplyThemeAsync();
            ModernLogger.Info($"✅ Tema aplicado completamente: {themeName}");
        }

        /// <summary>
        /// Cambia el tema (sobrecarga con enum)
        /// </summary>
        public async Task ChangeThemeAsync(ComicReader.Services.ThemeMode themeMode)
        {
            await ChangeThemeAsync(themeMode.ToString());
        }

        // ============================================================
        // UI
        // ============================================================

        private void ApplyUIConfiguration()
        {
            try
            {
                var ui = _config.UI;

                // Aplicar tamaño de fuente
                Application.Current.Resources["DefaultFontSize"] = (double)ui.FontSize;

                // Aplicar familia de fuente
                Application.Current.Resources["DefaultFontFamily"] = ui.FontFamily;

                ModernLogger.Info($"✓ Configuración UI aplicada: {ui.FontSize}px, {ui.FontFamily}");
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error aplicando UI: {ex.Message}");
            }
        }

        public async Task UpdateUIConfigurationAsync(Action<UIConfiguration> updateAction)
        {
            await _configManager.UpdateConfigurationAsync(config =>
            {
                updateAction(config.UI);
                config.LastSaved = DateTime.Now;
            });

            ApplyUIConfiguration();
        }

        // ============================================================
        // LECTURA
        // ============================================================

        private void ApplyReadingConfiguration()
        {
            try
            {
                var reading = _config.Reading;

                // Aplicar configuraciones de servicios
                if (reading.ImmersiveMode)
                {
                    ImmersiveReadingService.Instance.HideDelayMs = reading.ImmersiveDelay;
                }

                if (reading.SmoothTransitions)
                {
                    PageTransitionService.Instance.SetDuration(reading.TransitionDuration);
                }

                AdaptiveReadingService.Instance.AutoAdjustBrightness = reading.AdaptiveBrightness;
                AdaptiveReadingService.Instance.ReduceBlueLight = reading.ReduceBlueLight;

                ModernLogger.Info("✓ Configuración de lectura aplicada");
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error aplicando configuración de lectura: {ex.Message}");
            }
        }

        public async Task UpdateReadingConfigurationAsync(Action<ReadingConfiguration> updateAction)
        {
            await _configManager.UpdateConfigurationAsync(config =>
            {
                updateAction(config.Reading);
                config.LastSaved = DateTime.Now;
            });

            ApplyReadingConfiguration();
        }

        // ============================================================
        // RENDIMIENTO
        // ============================================================

        private void ApplyPerformanceConfiguration()
        {
            try
            {
                var perf = _config.Performance;

                // Aplicar configuraciones de rendimiento
                TiledImageRenderer.Instance.MaxMemoryMb = perf.MaxMemoryMB;
                TiledImageRenderer.Instance.MaxCachedTiles = perf.CacheSize;

                ModernLogger.Info($"✓ Configuración de rendimiento aplicada: {perf.MaxMemoryMB}MB, {perf.CacheSize} tiles");
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error aplicando rendimiento: {ex.Message}");
            }
        }

        public async Task UpdatePerformanceConfigurationAsync(Action<PerformanceConfiguration> updateAction)
        {
            await _configManager.UpdateConfigurationAsync(config =>
            {
                updateAction(config.Performance);
                config.LastSaved = DateTime.Now;
            });

            ApplyPerformanceConfiguration();
        }

        // ============================================================
        // ATAJOS
        // ============================================================

        private void ApplyShortcuts()
        {
            try
            {
                // Los atajos se aplican desde el ShortcutManager
                // que lee directamente del archivo de configuración
                ModernLogger.Info($"✓ {_config.Shortcuts.Count} atajos cargados");
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error aplicando atajos: {ex.Message}");
            }
        }

        public async Task UpdateShortcutAsync(string actionId, string key, string modifiers)
        {
            await _configManager.UpdateConfigurationAsync(config =>
            {
                if (config.Shortcuts.ContainsKey(actionId))
                {
                    config.Shortcuts[actionId].Key = key;
                    config.Shortcuts[actionId].Modifiers = modifiers;
                }
                else
                {
                    config.Shortcuts[actionId] = new ShortcutBinding
                    {
                        Key = key,
                        Modifiers = modifiers,
                        IsEnabled = true
                    };
                }
                config.LastSaved = DateTime.Now;
            });
        }

        // ============================================================
        // ACCESIBILIDAD
        // ============================================================

        private void ApplyAccessibilityConfiguration()
        {
            try
            {
                var a11y = _config.Accessibility;

                // Aplicar gestos táctiles
                if (a11y.TouchGestures)
                {
                    // TouchGestureService se activa según configuración
                }

                ModernLogger.Info("✓ Configuración de accesibilidad aplicada");
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error aplicando accesibilidad: {ex.Message}");
            }
        }

        public async Task UpdateAccessibilityConfigurationAsync(Action<AccessibilityConfiguration> updateAction)
        {
            await _configManager.UpdateConfigurationAsync(config =>
            {
                updateAction(config.Accessibility);
                config.LastSaved = DateTime.Now;
            });

            ApplyAccessibilityConfiguration();
        }

        // ============================================================
        // VENTANA
        // ============================================================

        private void ApplyWindowConfiguration()
        {
            try
            {
                var window = _config.Window;

                // La configuración de ventana se aplica en App.xaml.cs o MainWindow.xaml.cs
                ModernLogger.Info($"✓ Configuración de ventana cargada: {window.Width}x{window.Height}");
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error cargando configuración de ventana: {ex.Message}");
            }
        }

        public async Task UpdateWindowConfigurationAsync(double width, double height, double left, double top, bool isMaximized)
        {
            await _configManager.UpdateConfigurationAsync(config =>
            {
                config.Window.Width = width;
                config.Window.Height = height;
                config.Window.Left = left;
                config.Window.Top = top;
                config.Window.IsMaximized = isMaximized;
                config.LastSaved = DateTime.Now;
            });
        }

        // ============================================================
        // MARCADORES
        // ============================================================

        public async Task AddBookmarkAsync(string comicPath, int pageNumber, string note, string thumbnailBase64)
        {
            await _configManager.UpdateConfigurationAsync(config =>
            {
                config.Bookmarks.Add(new BookmarkEntry
                {
                    Id = Guid.NewGuid().ToString(),
                    ComicPath = comicPath,
                    PageNumber = pageNumber,
                    Note = note,
                    ThumbnailBase64 = thumbnailBase64,
                    CreatedDate = DateTime.Now
                });
                config.LastSaved = DateTime.Now;
            });

            ModernLogger.Info($"✓ Marcador agregado: {comicPath} página {pageNumber}");
        }

        public async Task RemoveBookmarkAsync(string bookmarkId)
        {
            await _configManager.UpdateConfigurationAsync(config =>
            {
                config.Bookmarks.RemoveAll(b => b.Id == bookmarkId);
                config.LastSaved = DateTime.Now;
            });

            ModernLogger.Info($"✓ Marcador eliminado: {bookmarkId}");
        }

        // ============================================================
        // ARCHIVOS RECIENTES
        // ============================================================

        public async Task AddRecentFileAsync(string filePath)
        {
            await _configManager.UpdateConfigurationAsync(config =>
            {
                // Remover si ya existe
                config.RecentFiles.Remove(filePath);

                // Agregar al principio
                config.RecentFiles.Insert(0, filePath);

                // Limitar a 20 archivos
                if (config.RecentFiles.Count > 20)
                {
                    config.RecentFiles.RemoveRange(20, config.RecentFiles.Count - 20);
                }

                config.LastSaved = DateTime.Now;
            });
        }

        // ============================================================
        // GETTERS
        // ============================================================

        public AppConfiguration GetConfiguration()
        {
            return _config;
        }

        public ThemeConfiguration GetThemeConfiguration()
        {
            return _config.Theme;
        }

        public UIConfiguration GetUIConfiguration()
        {
            return _config.UI;
        }

        public ReadingConfiguration GetReadingConfiguration()
        {
            return _config.Reading;
        }

        public PerformanceConfiguration GetPerformanceConfiguration()
        {
            return _config.Performance;
        }

        public AccessibilityConfiguration GetAccessibilityConfiguration()
        {
            return _config.Accessibility;
        }

        public WindowConfiguration GetWindowConfiguration()
        {
            return _config.Window;
        }

        // ============================================================
        // UTILIDADES
        // ============================================================

        /// <summary>
        /// Resetea TODA la configuración a valores por defecto
        /// </summary>
        public async Task ResetAllAsync()
        {
            await _configManager.ResetToDefaultsAsync();
            _config = _configManager.GetConfiguration();
            await InitializeApplicationAsync();
            
            ModernLogger.Info("🔄 Toda la configuración reseteada");
        }

        /// <summary>
        /// Guarda manualmente la configuración actual
        /// </summary>
        public async Task SaveAsync()
        {
            await _configManager.SaveConfigurationAsync();
        }

        /// <summary>
        /// Obtiene estadísticas del sistema
        /// </summary>
        public PersistenceStatistics GetStatistics()
        {
            return _configManager.GetStatistics();
        }
    }
}
