using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace ComicReader.Services.Persistence
{
    /// <summary>
    /// Configuración completa de la aplicación
    /// Modelo único de persistencia para TODAS las configuraciones
    /// </summary>
    public class AppConfiguration
    {
        // ============================================================
        // METADATA
        // ============================================================
        
        [JsonPropertyName("version")]
        public string Version { get; set; } = "2.0.0";

        [JsonPropertyName("lastSaved")]
        public DateTime LastSaved { get; set; } = DateTime.Now;

        [JsonPropertyName("appVersion")]
        public string AppVersion { get; set; } = "2.0.0";

        // ============================================================
        // TEMA Y APARIENCIA
        // ============================================================

        [JsonPropertyName("theme")]
        public ThemeConfiguration Theme { get; set; } = new ThemeConfiguration();

        // ============================================================
        // INTERFAZ DE USUARIO
        // ============================================================

        [JsonPropertyName("ui")]
        public UIConfiguration UI { get; set; } = new UIConfiguration();

        // ============================================================
        // LECTURA
        // ============================================================

        [JsonPropertyName("reading")]
        public ReadingConfiguration Reading { get; set; } = new ReadingConfiguration();

        // ============================================================
        // RENDIMIENTO
        // ============================================================

        [JsonPropertyName("performance")]
        public PerformanceConfiguration Performance { get; set; } = new PerformanceConfiguration();

        // ============================================================
        // ATAJOS DE TECLADO
        // ============================================================

        [JsonPropertyName("shortcuts")]
        public Dictionary<string, ShortcutBinding> Shortcuts { get; set; } = new Dictionary<string, ShortcutBinding>();

        // ============================================================
        // ACCESIBILIDAD
        // ============================================================

        [JsonPropertyName("accessibility")]
        public AccessibilityConfiguration Accessibility { get; set; } = new AccessibilityConfiguration();

        // ============================================================
        // FAVORITOS Y MARCADORES
        // ============================================================

        [JsonPropertyName("bookmarks")]
        public List<BookmarkEntry> Bookmarks { get; set; } = new List<BookmarkEntry>();

        // ============================================================
        // HISTORIAL RECIENTE
        // ============================================================

        [JsonPropertyName("recentFiles")]
        public List<string> RecentFiles { get; set; } = new List<string>();

        // ============================================================
        // VENTANA
        // ============================================================

        [JsonPropertyName("window")]
        public WindowConfiguration Window { get; set; } = new WindowConfiguration();

        // ============================================================
        // MÉTODOS
        // ============================================================

        /// <summary>
        /// Crea una configuración con valores por defecto
        /// </summary>
        public static AppConfiguration CreateDefault()
        {
            return new AppConfiguration
            {
                Version = "2.0.0",
                LastSaved = DateTime.Now,
                AppVersion = "2.0.0",
                Theme = ThemeConfiguration.CreateDefault(),
                UI = UIConfiguration.CreateDefault(),
                Reading = ReadingConfiguration.CreateDefault(),
                Performance = PerformanceConfiguration.CreateDefault(),
                Shortcuts = CreateDefaultShortcuts(),
                Accessibility = AccessibilityConfiguration.CreateDefault(),
                Bookmarks = new List<BookmarkEntry>(),
                RecentFiles = new List<string>(),
                Window = WindowConfiguration.CreateDefault()
            };
        }

        /// <summary>
        /// Valida la configuración
        /// </summary>
        public bool Validate()
        {
            try
            {
                // Validar version
                if (string.IsNullOrEmpty(Version))
                    return false;

                // Validar sub-configuraciones
                if (Theme == null || !Theme.Validate())
                    return false;

                if (UI == null || !UI.Validate())
                    return false;

                if (Reading == null || !Reading.Validate())
                    return false;

                if (Performance == null || !Performance.Validate())
                    return false;

                if (Shortcuts == null)
                    return false;

                if (Accessibility == null || !Accessibility.Validate())
                    return false;

                if (Window == null || !Window.Validate())
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Crea atajos por defecto
        /// </summary>
        private static Dictionary<string, ShortcutBinding> CreateDefaultShortcuts()
        {
            return new Dictionary<string, ShortcutBinding>
            {
                ["NextPage"] = new ShortcutBinding { Key = "Right", Modifiers = "None", IsEnabled = true },
                ["PrevPage"] = new ShortcutBinding { Key = "Left", Modifiers = "None", IsEnabled = true },
                ["FirstPage"] = new ShortcutBinding { Key = "Home", Modifiers = "None", IsEnabled = true },
                ["LastPage"] = new ShortcutBinding { Key = "End", Modifiers = "None", IsEnabled = true },
                ["ZoomIn"] = new ShortcutBinding { Key = "Add", Modifiers = "Control", IsEnabled = true },
                ["ZoomOut"] = new ShortcutBinding { Key = "Subtract", Modifiers = "Control", IsEnabled = true },
                ["FullScreen"] = new ShortcutBinding { Key = "F11", Modifiers = "None", IsEnabled = true },
                ["Immersive"] = new ShortcutBinding { Key = "I", Modifiers = "Control", IsEnabled = true },
                ["OpenFile"] = new ShortcutBinding { Key = "O", Modifiers = "Control", IsEnabled = true },
                ["Settings"] = new ShortcutBinding { Key = "OemComma", Modifiers = "Control", IsEnabled = true }
            };
        }
    }

    // ============================================================
    // CONFIGURACIÓN DE TEMA
    // ============================================================

    public class ThemeConfiguration
    {
        [JsonPropertyName("currentTheme")]
        public string CurrentTheme { get; set; } = "Dark";

        [JsonPropertyName("accentColor")]
        public string AccentColor { get; set; } = "#FF0078D4";

        [JsonPropertyName("useSystemTheme")]
        public bool UseSystemTheme { get; set; } = false;

        [JsonPropertyName("customColors")]
        public Dictionary<string, string> CustomColors { get; set; } = new Dictionary<string, string>();

        public static ThemeConfiguration CreateDefault()
        {
            return new ThemeConfiguration
            {
                CurrentTheme = "Dark",
                AccentColor = "#FF0078D4",
                UseSystemTheme = false,
                CustomColors = new Dictionary<string, string>()
            };
        }

        public bool Validate()
        {
            return !string.IsNullOrEmpty(CurrentTheme);
        }
    }

    // ============================================================
    // CONFIGURACIÓN DE UI
    // ============================================================

    public class UIConfiguration
    {
        [JsonPropertyName("language")]
        public string Language { get; set; } = "es-ES";

        [JsonPropertyName("fontSize")]
        public int FontSize { get; set; } = 14;

        [JsonPropertyName("fontFamily")]
        public string FontFamily { get; set; } = "Segoe UI";

        [JsonPropertyName("showToolbar")]
        public bool ShowToolbar { get; set; } = true;

        [JsonPropertyName("showStatusBar")]
        public bool ShowStatusBar { get; set; } = true;

        [JsonPropertyName("showSidebar")]
        public bool ShowSidebar { get; set; } = true;

        [JsonPropertyName("compactMode")]
        public bool CompactMode { get; set; } = false;

        [JsonPropertyName("animations")]
        public bool Animations { get; set; } = true;

        public static UIConfiguration CreateDefault()
        {
            return new UIConfiguration
            {
                Language = "es-ES",
                FontSize = 14,
                FontFamily = "Segoe UI",
                ShowToolbar = true,
                ShowStatusBar = true,
                ShowSidebar = true,
                CompactMode = false,
                Animations = true
            };
        }

        public bool Validate()
        {
            return FontSize >= 8 && FontSize <= 32 && !string.IsNullOrEmpty(Language);
        }
    }

    // ============================================================
    // CONFIGURACIÓN DE LECTURA
    // ============================================================

    public class ReadingConfiguration
    {
        [JsonPropertyName("readingMode")]
        public string ReadingMode { get; set; } = "Single";

        [JsonPropertyName("readingDirection")]
        public string ReadingDirection { get; set; } = "LeftToRight";

        [JsonPropertyName("fitMode")]
        public string FitMode { get; set; } = "FitWidth";

        [JsonPropertyName("backgroundColor")]
        public string BackgroundColor { get; set; } = "#FF1E1E1E";

        [JsonPropertyName("immersiveMode")]
        public bool ImmersiveMode { get; set; } = false;

        [JsonPropertyName("immersiveDelay")]
        public int ImmersiveDelay { get; set; } = 3000;

        [JsonPropertyName("adaptiveBrightness")]
        public bool AdaptiveBrightness { get; set; } = true;

        [JsonPropertyName("reduceBlueLight")]
        public bool ReduceBlueLight { get; set; } = true;

        [JsonPropertyName("smoothTransitions")]
        public bool SmoothTransitions { get; set; } = true;

        [JsonPropertyName("transitionType")]
        public string TransitionType { get; set; } = "Fade";

        [JsonPropertyName("transitionDuration")]
        public int TransitionDuration { get; set; } = 300;

        public static ReadingConfiguration CreateDefault()
        {
            return new ReadingConfiguration
            {
                ReadingMode = "Single",
                ReadingDirection = "LeftToRight",
                FitMode = "FitWidth",
                BackgroundColor = "#FF1E1E1E",
                ImmersiveMode = false,
                ImmersiveDelay = 3000,
                AdaptiveBrightness = true,
                ReduceBlueLight = true,
                SmoothTransitions = true,
                TransitionType = "Fade",
                TransitionDuration = 300
            };
        }

        public bool Validate()
        {
            return TransitionDuration >= 50 && TransitionDuration <= 2000 &&
                   ImmersiveDelay >= 1000 && ImmersiveDelay <= 10000;
        }
    }

    // ============================================================
    // CONFIGURACIÓN DE RENDIMIENTO
    // ============================================================

    public class PerformanceConfiguration
    {
        [JsonPropertyName("preloadPages")]
        public int PreloadPages { get; set; } = 4;

        [JsonPropertyName("cacheSize")]
        public int CacheSize { get; set; } = 50;

        [JsonPropertyName("maxMemoryMB")]
        public int MaxMemoryMB { get; set; } = 500;

        [JsonPropertyName("useGPU")]
        public bool UseGPU { get; set; } = true;

        [JsonPropertyName("compressionQuality")]
        public int CompressionQuality { get; set; } = 90;

        [JsonPropertyName("intelligentPreload")]
        public bool IntelligentPreload { get; set; } = true;

        public static PerformanceConfiguration CreateDefault()
        {
            return new PerformanceConfiguration
            {
                PreloadPages = 4,
                CacheSize = 50,
                MaxMemoryMB = 500,
                UseGPU = true,
                CompressionQuality = 90,
                IntelligentPreload = true
            };
        }

        public bool Validate()
        {
            return PreloadPages >= 0 && PreloadPages <= 10 &&
                   CacheSize >= 10 && CacheSize <= 100 &&
                   MaxMemoryMB >= 100 && MaxMemoryMB <= 2000 &&
                   CompressionQuality >= 50 && CompressionQuality <= 100;
        }
    }

    // ============================================================
    // CONFIGURACIÓN DE ACCESIBILIDAD
    // ============================================================

    public class AccessibilityConfiguration
    {
        [JsonPropertyName("highContrast")]
        public bool HighContrast { get; set; } = false;

        [JsonPropertyName("screenReader")]
        public bool ScreenReader { get; set; } = false;

        [JsonPropertyName("largeText")]
        public bool LargeText { get; set; } = false;

        [JsonPropertyName("keyboardNavigation")]
        public bool KeyboardNavigation { get; set; } = true;

        [JsonPropertyName("touchGestures")]
        public bool TouchGestures { get; set; } = true;

        [JsonPropertyName("audioFeedback")]
        public bool AudioFeedback { get; set; } = false;

        public static AccessibilityConfiguration CreateDefault()
        {
            return new AccessibilityConfiguration
            {
                HighContrast = false,
                ScreenReader = false,
                LargeText = false,
                KeyboardNavigation = true,
                TouchGestures = true,
                AudioFeedback = false
            };
        }

        public bool Validate()
        {
            return true; // Todas las opciones son válidas
        }
    }

    // ============================================================
    // CONFIGURACIÓN DE VENTANA
    // ============================================================

    public class WindowConfiguration
    {
        [JsonPropertyName("width")]
        public double Width { get; set; } = 1200;

        [JsonPropertyName("height")]
        public double Height { get; set; } = 800;

        [JsonPropertyName("left")]
        public double Left { get; set; } = 100;

        [JsonPropertyName("top")]
        public double Top { get; set; } = 100;

        [JsonPropertyName("isMaximized")]
        public bool IsMaximized { get; set; } = false;

        [JsonPropertyName("isFullScreen")]
        public bool IsFullScreen { get; set; } = false;

        public static WindowConfiguration CreateDefault()
        {
            return new WindowConfiguration
            {
                Width = 1200,
                Height = 800,
                Left = 100,
                Top = 100,
                IsMaximized = false,
                IsFullScreen = false
            };
        }

        public bool Validate()
        {
            return Width >= 800 && Width <= 4000 &&
                   Height >= 600 && Height <= 3000;
        }
    }

    // ============================================================
    // ATAJO DE TECLADO
    // ============================================================

    public class ShortcutBinding
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("modifiers")]
        public string Modifiers { get; set; }

        [JsonPropertyName("isEnabled")]
        public bool IsEnabled { get; set; } = true;
    }

    // ============================================================
    // ENTRADA DE MARCADOR
    // ============================================================

    public class BookmarkEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("comicPath")]
        public string ComicPath { get; set; }

        [JsonPropertyName("pageNumber")]
        public int PageNumber { get; set; }

        [JsonPropertyName("note")]
        public string Note { get; set; }

        [JsonPropertyName("createdDate")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        [JsonPropertyName("thumbnailBase64")]
        public string ThumbnailBase64 { get; set; }
    }
}
