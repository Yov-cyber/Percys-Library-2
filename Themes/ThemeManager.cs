using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using ComicReader.Services;
using ComicReader.Utils;

namespace ComicReader.Themes
{
    public static class ThemeManager
    {
        private static readonly Dictionary<ThemeMode, ResourceDictionary> _themes = 
            new Dictionary<ThemeMode, ResourceDictionary>();
        
        private static ThemeMode _currentTheme = ThemeMode.Dark;
        public static event Action<ThemeMode> ThemeChanged;

        static ThemeManager()
        {
            LoadAllThemes();
        }

        // Keys the application expects to exist for each theme (colors and brushes)
        private static readonly string[] RequiredColorKeys = new[] {
            "PrimaryColor", "SecondaryColor", "AccentColor", "ErrorColor",
            "WindowBackgroundColor", "PanelBackgroundColor", "HeaderBackgroundColor", "InputBackgroundColor",
            "TextColor", "SecondaryTextColor", "DisabledTextColor",
            "BorderColor", "ItemHoverColor", "ItemSelectedColor", "CurrentPageColor",
            "BookmarkColor", "CurrentPageBorderColor"
        };

        public static ThemeMode CurrentTheme
        {
            get => _currentTheme;
            set
            {
                if (_currentTheme != value)
                {
                    _currentTheme = value;
                    ApplyTheme(value);
                    ThemeChanged?.Invoke(value);
                }
            }
        }

        private static void LoadAllThemes()
        {
            _themes[ThemeMode.Light] = CreateLightTheme();
            _themes[ThemeMode.Dark] = CreateDarkTheme();
            _themes[ThemeMode.Comic] = CreateComicTheme();
            _themes[ThemeMode.Sepia] = CreateSepiaTheme();
            _themes[ThemeMode.HighContrast] = CreateHighContrastTheme();

            // Helper to create simple palette-based themes
            ResourceDictionary P(Color p, Color s, Color a, Color windowBg, Color panelBg, Color text)
            {
                var theme = new ResourceDictionary();
                theme["PrimaryColor"] = p;
                theme["SecondaryColor"] = s;
                theme["AccentColor"] = a;
                theme["WindowBackgroundColor"] = windowBg;
                theme["PanelBackgroundColor"] = panelBg;
                theme["TextColor"] = text;
                CreateBrushesFromColors(theme);
                return theme;
            }

            // Marvel-inspired
            _themes[ThemeMode.StarkRed] = P(Color.FromRgb(196, 18, 18), Color.FromRgb(255, 184, 0), Color.FromRgb(226, 62, 62), Color.FromRgb(24, 24, 24), Color.FromRgb(32, 32, 32), Color.FromRgb(245,245,245));
            _themes[ThemeMode.PatriotBlue] = P(Color.FromRgb(11, 84, 142), Color.FromRgb(215,39,61), Color.FromRgb(42,111,151), Color.FromRgb(8,16,30), Color.FromRgb(18,28,46), Color.FromRgb(245,245,245));
            _themes[ThemeMode.ArañaRoja] = P(Color.FromRgb(180, 20, 20), Color.FromRgb(30,30,30), Color.FromRgb(255,50,50), Color.FromRgb(16,18,27), Color.FromRgb(22,24,34), Color.FromRgb(240,240,240));
            _themes[ThemeMode.GammaGreen] = P(Color.FromRgb(32, 160, 72), Color.FromRgb(20,30,20), Color.FromRgb(34,197,94), Color.FromRgb(8,18,12), Color.FromRgb(16,28,18), Color.FromRgb(240,255,240));
            _themes[ThemeMode.AsgardGold] = P(Color.FromRgb(255, 196, 37), Color.FromRgb(142,68,173), Color.FromRgb(255,127,39), Color.FromRgb(24,20,16), Color.FromRgb(34,30,26), Color.FromRgb(250,240,220));

            // DC-inspired
            _themes[ThemeMode.BatNight] = P(Color.FromRgb(10, 14, 22), Color.FromRgb(40, 40, 48), Color.FromRgb(25,118,210), Color.FromRgb(6,8,12), Color.FromRgb(12,14,20), Color.FromRgb(230,230,235));
            _themes[ThemeMode.KryptonBlue] = P(Color.FromRgb(17, 94, 168), Color.FromRgb(240,200,0), Color.FromRgb(100,181,246), Color.FromRgb(10,18,30), Color.FromRgb(16,28,44), Color.FromRgb(245,245,255));
            _themes[ThemeMode.MetroNeon] = P(Color.FromRgb(4, 120, 255), Color.FromRgb(255, 95, 87), Color.FromRgb(0,188,212), Color.FromRgb(6,10,20), Color.FromRgb(12,18,32), Color.FromRgb(240,248,255));
            _themes[ThemeMode.AmazonEmerald] = P(Color.FromRgb(18, 136, 86), Color.FromRgb(255, 200, 200), Color.FromRgb(34,197,94), Color.FromRgb(10,18,12), Color.FromRgb(14,28,18), Color.FromRgb(240,250,245));
            _themes[ThemeMode.OracleGray] = P(Color.FromRgb(90,90,96), Color.FromRgb(200,200,208), Color.FromRgb(129,199,132), Color.FromRgb(18,18,20), Color.FromRgb(28,28,30), Color.FromRgb(235,235,235));

            // Manga styles
            _themes[ThemeMode.ShonenBurst] = P(Color.FromRgb(255, 80, 80), Color.FromRgb(255, 215, 0), Color.FromRgb(255, 48, 96), Color.FromRgb(18,18,22), Color.FromRgb(28,26,30), Color.FromRgb(250,250,250));
            _themes[ThemeMode.ShojoBloom] = P(Color.FromRgb(255, 150, 190), Color.FromRgb(255, 220, 230), Color.FromRgb(255, 130, 180), Color.FromRgb(255,245,250), Color.FromRgb(255,250,252), Color.FromRgb(30,14,24));
            _themes[ThemeMode.SeinenNoir] = P(Color.FromRgb(40,40,40), Color.FromRgb(120,120,120), Color.FromRgb(220,220,220), Color.FromRgb(8,8,8), Color.FromRgb(14,14,14), Color.FromRgb(230,230,230));
            _themes[ThemeMode.GekigaSepia] = P(Color.FromRgb(160,120,80), Color.FromRgb(200,180,150), Color.FromRgb(129,94,60), Color.FromRgb(245,240,233), Color.FromRgb(250,245,238), Color.FromRgb(40,30,20));
            _themes[ThemeMode.MangaInk] = P(Color.FromRgb(10,10,10), Color.FromRgb(240,240,240), Color.FromRgb(60,60,60), Color.FromRgb(255,255,255), Color.FromRgb(250,250,250), Color.FromRgb(0,0,0));

            // Classic ages & pop
            _themes[ThemeMode.GoldenAge] = P(Color.FromRgb(230, 170, 0), Color.FromRgb(180,80,40), Color.FromRgb(255,204,0), Color.FromRgb(255,250,230), Color.FromRgb(255,245,220), Color.FromRgb(28,24,18));
            _themes[ThemeMode.SilverAge] = P(Color.FromRgb(180, 200, 220), Color.FromRgb(80,80,200), Color.FromRgb(129,199,132), Color.FromRgb(245,248,250), Color.FromRgb(238,242,246), Color.FromRgb(18,22,28));
            _themes[ThemeMode.BronzeAge] = P(Color.FromRgb(200,130,60), Color.FromRgb(90,60,40), Color.FromRgb(255,152,0), Color.FromRgb(250,240,230), Color.FromRgb(244,234,220), Color.FromRgb(36,28,20));
            _themes[ThemeMode.PopArt] = P(Color.FromRgb(255, 64, 129), Color.FromRgb(0, 188, 212), Color.FromRgb(255, 213, 0), Color.FromRgb(20,20,40), Color.FromRgb(28,28,56), Color.FromRgb(250,250,250));
            _themes[ThemeMode.Pulps] = P(Color.FromRgb(120,30,20), Color.FromRgb(200,80,40), Color.FromRgb(220,60,60), Color.FromRgb(32,24,20), Color.FromRgb(48,36,30), Color.FromRgb(240,230,220));

            // Retro / modern hybrid
            _themes[ThemeMode.NoirStrip] = P(Color.FromRgb(20,20,20), Color.FromRgb(80,80,80), Color.FromRgb(200,200,200), Color.FromRgb(8,8,8), Color.FromRgb(12,12,12), Color.FromRgb(235,235,235));
            _themes[ThemeMode.PastelRetro] = P(Color.FromRgb(255, 179, 186), Color.FromRgb(255, 223, 186), Color.FromRgb(255, 255, 186), Color.FromRgb(255,250,245), Color.FromRgb(255,248,246), Color.FromRgb(38,28,30));
            _themes[ThemeMode.NeonCyber] = P(Color.FromRgb(96, 255, 243), Color.FromRgb(255, 105, 180), Color.FromRgb(4,120,255), Color.FromRgb(6,6,12), Color.FromRgb(10,10,18), Color.FromRgb(230,240,255));
            _themes[ThemeMode.Vaporwave] = P(Color.FromRgb(255, 80, 192), Color.FromRgb(0, 200, 255), Color.FromRgb(180, 120, 255), Color.FromRgb(12,6,24), Color.FromRgb(18,12,36), Color.FromRgb(245,240,250));
            _themes[ThemeMode.Retro80s] = P(Color.FromRgb(255, 64, 129), Color.FromRgb(255, 170, 0), Color.FromRgb(0, 200, 255), Color.FromRgb(14,10,20), Color.FromRgb(22,18,36), Color.FromRgb(245,245,250));

            // Misc
            _themes[ThemeMode.ComicPop] = P(Color.FromRgb(255, 99, 71), Color.FromRgb(65,105,225), Color.FromRgb(255, 215, 0), Color.FromRgb(20,20,28), Color.FromRgb(30,30,38), Color.FromRgb(245,245,245));
            _themes[ThemeMode.VintagePaper] = P(Color.FromRgb(190,150,110), Color.FromRgb(140,110,90), Color.FromRgb(220,180,140), Color.FromRgb(250,245,235), Color.FromRgb(255,250,240), Color.FromRgb(38,28,18));
            _themes[ThemeMode.CelShade] = P(Color.FromRgb(255, 200, 80), Color.FromRgb(200, 60, 140), Color.FromRgb(100,180,255), Color.FromRgb(18,18,20), Color.FromRgb(28,28,30), Color.FromRgb(245,245,245));
            _themes[ThemeMode.CartoonBright] = P(Color.FromRgb(255, 140, 0), Color.FromRgb(255, 64, 129), Color.FromRgb(0, 188, 212), Color.FromRgb(250,250,250), Color.FromRgb(255,255,255), Color.FromRgb(18,18,18));
            _themes[ThemeMode.MonochromeHighContrast] = P(Color.FromRgb(0,0,0), Color.FromRgb(255,255,255), Color.FromRgb(255,255,255), Color.FromRgb(0,0,0), Color.FromRgb(24,24,24), Color.FromRgb(255,255,255));
            _themes[ThemeMode.PastelGentle] = P(Color.FromRgb(200,220,240), Color.FromRgb(240,200,220), Color.FromRgb(220,240,200), Color.FromRgb(250,250,250), Color.FromRgb(255,255,255), Color.FromRgb(30,30,30));

            // TEST: Rosa fucsia brillante para verificar persistencia visual
            _themes[ThemeMode.TestHotPink] = P(
                Color.FromRgb(255, 20, 147),   // PrimaryColor - rosa fucsia brillante
                Color.FromRgb(255, 255, 0),    // SecondaryColor - amarillo brillante
                Color.FromRgb(255, 20, 147),   // AccentColor - rosa fucsia
                Color.FromRgb(139, 0, 139),    // WindowBackgroundColor - morado oscuro
                Color.FromRgb(160, 32, 160),   // PanelBackgroundColor - morado más claro
                Color.FromRgb(255, 255, 255)   // TextColor - blanco
            );

            // Exclusive branded theme for the app: "Percy's Library"
            // Palette uses the app's signature deep-navy background and bright blue accent,
            // with a warm secondary for highlights. Ensure contrast-safe choices.
            _themes[ThemeMode.PercysLibrary] = P(
                Color.FromRgb(59, 130, 246),   // PrimaryColor - brand blue (#3B82F6)
                Color.FromRgb(255, 211, 102),  // SecondaryColor - warm accent (golden)
                Color.FromRgb(59, 130, 246),   // AccentColor - brand blue
                Color.FromRgb(14, 23, 41),     // WindowBackgroundColor - deep navy (#0E1729)
                Color.FromRgb(16, 24, 41),     // PanelBackgroundColor - slightly lighter panel
                Color.FromRgb(230, 236, 248)   // TextColor - soft light text (#E6ECF8)
            );

            // Validate and fill missing keys for all themes
            foreach (var kv in _themes)
            {
                try { EnsureThemeComplete(kv.Value); } catch { }
            }
        }

        public static void ApplyTheme(ThemeMode theme)
        {
            if (_themes.TryGetValue(theme, out var themeResource))
            {
                var app = Application.Current;
                if (app?.Resources != null)
                {
                    // Phase 1: ya no se borran todos los Brush/Color del diccionario global.
                    // Tokens.xaml es la fuente de verdad; el tema sólo SOBREESCRIBE los keys
                    // que el tema explícitamente define. Esto evita borrar brushes que la
                    // estética unificada define y que el tema no reescribe.
                    foreach (var key in themeResource.Keys)
                    {
                        app.Resources[key] = themeResource[key];
                    }
                    
                    // Actualizar el tema actual
                    _currentTheme = theme;
                    
                    // LOG: Mostrar colores clave que se están aplicando
                    try
                    {
                        if (themeResource.Contains("WindowBackgroundColor") && themeResource["WindowBackgroundColor"] is Color windowBg)
                        {
                            DevLogger.Info($"🎨 TEMA APLICADO: {theme}");
                            DevLogger.Info($"   WindowBackground: #{windowBg.R:X2}{windowBg.G:X2}{windowBg.B:X2}");
                            
                            if (themeResource.Contains("PanelBackgroundColor") && themeResource["PanelBackgroundColor"] is Color panelBg)
                                DevLogger.Info($"   PanelBackground: #{panelBg.R:X2}{panelBg.G:X2}{panelBg.B:X2}");
                            
                            if (themeResource.Contains("PrimaryColor") && themeResource["PrimaryColor"] is Color primary)
                                DevLogger.Info($"   Primary: #{primary.R:X2}{primary.G:X2}{primary.B:X2}");
                        }
                    }
                    catch { }
                    
                    // FORZAR ACTUALIZACIÓN VISUAL DE TODAS LAS VENTANAS
                    try
                    {
                        DevLogger.Info("→ Forzando actualización visual de ventanas...");
                        
                        // Método 1: Invalidar visual de todas las ventanas abiertas
                        foreach (Window window in app.Windows)
                        {
                            try
                            {
                                // Forzar re-evaluación de recursos
                                window.InvalidateVisual();
                                
                                // Forzar actualización de todos los elementos hijos
                                InvalidateVisualTree(window);
                                
                                DevLogger.Info($"  ✓ Ventana actualizada: {window.GetType().Name}");
                            }
                            catch (Exception winEx)
                            {
                                DevLogger.Error($"  ✗ Error actualizando ventana: {winEx.Message}");
                            }
                        }
                        
                        DevLogger.Info("✓ Actualización visual completada");
                    }
                    catch (Exception updateEx)
                    {
                        DevLogger.Error($"Error en actualización visual: {updateEx.Message}");
                    }
                    
                    // Notificar cambio de tema
                    try
                    {
                        ThemeChanged?.Invoke(theme);
                    }
                    catch { }
                }
            }
        }

        public static void ApplyAccent(string accentName)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;
                // Basic mapping of names to colors (extend as needed)
                System.Windows.Media.Color c = System.Windows.Media.Colors.CadetBlue;
                switch ((accentName ?? string.Empty).ToLowerInvariant())
                {
                    case "rojo": c = System.Windows.Media.Color.FromRgb(226, 62, 62); break;
                    case "azul": c = System.Windows.Media.Color.FromRgb(42, 111, 151); break;
                    case "amarillo": c = System.Windows.Media.Color.FromRgb(255, 214, 0); break;
                    case "verde": c = System.Windows.Media.Color.FromRgb(34, 197, 94); break;
                    case "naranja": c = System.Windows.Media.Color.FromRgb(255, 127, 39); break;
                    case "cyan": c = System.Windows.Media.Color.FromRgb(0, 188, 212); break;
                    default: c = System.Windows.Media.Colors.CadetBlue; break;
                }
                var brush = new System.Windows.Media.SolidColorBrush(c);
                brush.Freeze();
                app.Resources["AccentColor"] = c;
                app.Resources["AccentBrush"] = brush;
            }
            catch { }
        }

        /// <summary>
        /// Invalida recursivamente todo el árbol visual para forzar actualización
        /// </summary>
        private static void InvalidateVisualTree(System.Windows.DependencyObject obj)
        {
            if (obj == null) return;

            try
            {
                // Si es un UIElement, invalidar visual
                if (obj is System.Windows.UIElement uiElement)
                {
                    uiElement.InvalidateVisual();
                }

                // Si es un FrameworkElement, forzar actualización de layout
                if (obj is System.Windows.FrameworkElement frameworkElement)
                {
                    frameworkElement.InvalidateVisual();
                    frameworkElement.UpdateLayout();
                }

                // Recorrer todos los hijos
                int childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(obj);
                for (int i = 0; i < childrenCount; i++)
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(obj, i);
                    InvalidateVisualTree(child);
                }
            }
            catch
            {
                // Ignorar errores en elementos individuales
            }
        }

        private static ResourceDictionary CreateLightTheme()
        {
            var theme = new ResourceDictionary();

            // Colores primarios
            theme["PrimaryColor"] = Color.FromRgb(33, 150, 243);
            theme["SecondaryColor"] = Color.FromRgb(255, 193, 7);
            theme["AccentColor"] = Color.FromRgb(76, 175, 80);
            theme["ErrorColor"] = Color.FromRgb(244, 67, 54);

            // Colores de fondo
            theme["WindowBackgroundColor"] = Color.FromRgb(250, 250, 250);
            theme["PanelBackgroundColor"] = Color.FromRgb(255, 255, 255);
            theme["HeaderBackgroundColor"] = Color.FromRgb(240, 240, 240);
            theme["InputBackgroundColor"] = Color.FromRgb(248, 248, 248);

            // Colores de texto
            theme["TextColor"] = Color.FromRgb(33, 33, 33);
            theme["SecondaryTextColor"] = Color.FromRgb(117, 117, 117);
            theme["DisabledTextColor"] = Color.FromRgb(189, 189, 189);

            // Colores de interacción
            theme["BorderColor"] = Color.FromRgb(224, 224, 224);
            theme["ItemHoverColor"] = Color.FromRgb(245, 245, 245);
            theme["ItemSelectedColor"] = Color.FromRgb(227, 242, 253);
            theme["CurrentPageColor"] = Color.FromRgb(255, 249, 196);

            // Crear pinceles
            CreateBrushesFromColors(theme);
            return theme;
        }

        private static ResourceDictionary CreateDarkTheme()
        {
            // Phase 1: paleta única "Percy's Library Dark" — coincide con Tokens.xaml.
            // Acento azul (#3B82F6) reservado a acción primaria y progreso de lectura.
            // Sin acento amarillo / naranja / verde decorativo.
            var theme = new ResourceDictionary();

            // Acento + estados
            theme["PrimaryColor"] = Color.FromRgb(0x3B, 0x82, 0xF6);
            theme["SecondaryColor"] = Color.FromRgb(0x16, 0x18, 0x1D);
            theme["AccentColor"] = Color.FromRgb(0x3B, 0x82, 0xF6);
            theme["ErrorColor"] = Color.FromRgb(0xEF, 0x44, 0x44);

            // Superficies (Surface.Base / Raised / Overlay)
            theme["WindowBackgroundColor"] = Color.FromRgb(0x0E, 0x0F, 0x12);
            theme["PanelBackgroundColor"] = Color.FromRgb(0x16, 0x18, 0x1D);
            theme["HeaderBackgroundColor"] = Color.FromRgb(0x16, 0x18, 0x1D);
            theme["InputBackgroundColor"] = Color.FromRgb(0x16, 0x18, 0x1D);

            // Texto
            theme["TextColor"] = Color.FromRgb(0xEC, 0xED, 0xEE);
            theme["SecondaryTextColor"] = Color.FromRgb(0x9B, 0xA1, 0xA8);
            theme["DisabledTextColor"] = Color.FromRgb(0x5C, 0x61, 0x68);

            // Interacción
            theme["BorderColor"] = Color.FromRgb(0x26, 0x29, 0x32);
            theme["ItemHoverColor"] = Color.FromRgb(0x26, 0x29, 0x32);
            theme["ItemSelectedColor"] = Color.FromRgb(0x1B, 0x26, 0x38);
            theme["CurrentPageColor"] = Color.FromRgb(0x1B, 0x26, 0x38);
            theme["CurrentPageBorderColor"] = Color.FromRgb(0x3B, 0x82, 0xF6);
            theme["BookmarkColor"] = Color.FromRgb(0xF5, 0x9E, 0x0B);

            CreateBrushesFromColors(theme);
            return theme;
        }

        private static ResourceDictionary CreateComicTheme()
        {
            // Phase 1: el "Comic" theme original era el desorden naranja+púrpura+cyan
            // que disparaba el chaos visual. Eliminado. Usuarios con "Comic" guardado
            // como tema activo ahora ven la paleta limpia "Dark" hasta que Phase 4
            // colapse el selector de temas a una sola opción.
            return CreateDarkTheme();
        }

        private static ResourceDictionary CreateSepiaTheme()
        {
            var theme = new ResourceDictionary();

            // Paleta sepia clásica
            theme["PrimaryColor"] = Color.FromRgb(139, 69, 19);   // Sepia oscuro
            theme["SecondaryColor"] = Color.FromRgb(210, 180, 140); // Tan
            theme["AccentColor"] = Color.FromRgb(160, 82, 45);    // Marrón rojizo
            theme["ErrorColor"] = Color.FromRgb(178, 34, 34);

            // Fondos en tonos sepia
            theme["WindowBackgroundColor"] = Color.FromRgb(245, 235, 220);
            theme["PanelBackgroundColor"] = Color.FromRgb(250, 240, 230);
            theme["HeaderBackgroundColor"] = Color.FromRgb(222, 184, 135);
            theme["InputBackgroundColor"] = Color.FromRgb(255, 248, 240);

            // Textos en sepia
            theme["TextColor"] = Color.FromRgb(101, 67, 33);
            theme["SecondaryTextColor"] = Color.FromRgb(139, 119, 101);
            theme["DisabledTextColor"] = Color.FromRgb(188, 143, 143);

            // Interacciones en sepia
            theme["BorderColor"] = Color.FromRgb(205, 133, 63);
            theme["ItemHoverColor"] = Color.FromRgb(240, 230, 215);
            theme["ItemSelectedColor"] = Color.FromRgb(222, 184, 135);
            theme["CurrentPageColor"] = Color.FromRgb(255, 228, 181);

            CreateBrushesFromColors(theme);
            return theme;
        }

        private static ResourceDictionary CreateHighContrastTheme()
        {
            var theme = new ResourceDictionary();

            // Alto contraste para accesibilidad
            theme["PrimaryColor"] = Color.FromRgb(255, 255, 0);    // Amarillo brillante
            theme["SecondaryColor"] = Color.FromRgb(0, 255, 255);  // Cyan brillante
            theme["AccentColor"] = Color.FromRgb(255, 0, 255);     // Magenta
            theme["ErrorColor"] = Color.FromRgb(255, 0, 0);

            // Fondos con máximo contraste
            theme["WindowBackgroundColor"] = Color.FromRgb(0, 0, 0);
            theme["PanelBackgroundColor"] = Color.FromRgb(16, 16, 16);
            theme["HeaderBackgroundColor"] = Color.FromRgb(32, 32, 32);
            theme["InputBackgroundColor"] = Color.FromRgb(48, 48, 48);

            // Textos con máximo contraste
            theme["TextColor"] = Color.FromRgb(255, 255, 255);
            theme["SecondaryTextColor"] = Color.FromRgb(255, 255, 0);
            theme["DisabledTextColor"] = Color.FromRgb(128, 128, 128);

            // Interacciones con alto contraste
            theme["BorderColor"] = Color.FromRgb(255, 255, 255);
            theme["ItemHoverColor"] = Color.FromRgb(64, 64, 64);
            theme["ItemSelectedColor"] = Color.FromRgb(0, 0, 255);
            theme["CurrentPageColor"] = Color.FromRgb(255, 255, 0);

            CreateBrushesFromColors(theme);
            return theme;
        }

        private static void CreateBrushesFromColors(ResourceDictionary theme)
        {
            var colorsToProcess = new List<string>();
            
            foreach (var key in theme.Keys)
            {
                if (key.ToString().EndsWith("Color"))
                {
                    colorsToProcess.Add(key.ToString());
                }
            }

            foreach (var colorKey in colorsToProcess)
            {
                if (theme[colorKey] is Color color)
                {
                    var brushKey = colorKey.Replace("Color", "Brush");
                    var sb = new SolidColorBrush(color);
                    sb.Freeze();
                    theme[brushKey] = sb;

                    // Crear versiones con transparencia
                    var hoverBrushKey = colorKey.Replace("Color", "HoverBrush");
                    var hoverColor = Color.FromArgb(200, color.R, color.G, color.B);
                    var hb = new SolidColorBrush(hoverColor); hb.Freeze(); theme[hoverBrushKey] = hb;

                    var pressedBrushKey = colorKey.Replace("Color", "PressedBrush");
                    var pressedColor = Color.FromArgb(150, color.R, color.G, color.B);
                    var pb = new SolidColorBrush(pressedColor); pb.Freeze(); theme[pressedBrushKey] = pb;
                }
            }

            // Crear pincel especial para overlay
            var overlayColor = Color.FromArgb(128, 0, 0, 0);
            var overlay = new SolidColorBrush(overlayColor); overlay.Freeze(); theme["OverlayBackgroundBrush"] = overlay;

            // Pincel para elementos deshabilitados
            var disabledColor = Color.FromArgb(100, 128, 128, 128);
            var disabledBrush = new SolidColorBrush(disabledColor); disabledBrush.Freeze(); theme["DisabledBrush"] = disabledBrush;

            // ===== Tokens semanticos del sistema unificado (Phase 1) =====
            // Components.xaml referencia 'Surface.*', 'Text.*', 'Border.*',
            // 'Danger.*', 'Accent.*' como DynamicResource. Si no se generan
            // aqui se quedan congelados en los valores estaticos del tema
            // dark de Tokens.xaml al cambiar de tema. Mapeamos desde los
            // colores legacy que cada tema define en su constructor.
            void SetSemantic(string key, Color color)
            {
                var b = new SolidColorBrush(color);
                b.Freeze();
                theme[key] = b;
            }

            Color GetColor(string key, Color fallback)
            {
                return theme.Contains(key) && theme[key] is Color c ? c : fallback;
            }

            var windowBg = GetColor("WindowBackgroundColor", Color.FromRgb(0x0E, 0x0F, 0x12));
            var panelBg = GetColor("PanelBackgroundColor", Color.FromRgb(0x16, 0x18, 0x1D));
            var headerBg = GetColor("HeaderBackgroundColor", panelBg);
            var hoverBg = GetColor("ItemHoverColor", Color.FromRgb(0x26, 0x29, 0x32));
            var selectedBg = GetColor("ItemSelectedColor", Color.FromRgb(0x1B, 0x26, 0x38));
            var textColor = GetColor("TextColor", Color.FromRgb(0xEC, 0xED, 0xEE));
            var secondaryTextColor = GetColor("SecondaryTextColor", Color.FromRgb(0x9B, 0xA1, 0xA8));
            var borderColor = GetColor("BorderColor", Color.FromRgb(0x3A, 0x3F, 0x4A));
            var accentColor = GetColor("AccentColor", GetColor("PrimaryColor", Color.FromRgb(0x4D, 0x9D, 0xE0)));
            var errorColor = GetColor("ErrorColor", Color.FromRgb(0xE5, 0x4B, 0x4B));

            SetSemantic("Surface.Base.Brush", windowBg);
            SetSemantic("Surface.Raised.Brush", panelBg);
            SetSemantic("Surface.Overlay.Brush", headerBg);
            SetSemantic("Surface.Hover.Brush", hoverBg);
            SetSemantic("Surface.Selected.Brush", selectedBg);

            SetSemantic("Text.Primary.Brush", textColor);
            SetSemantic("Text.Secondary.Brush", secondaryTextColor);
            // OnAccent: contraste sobre el accent. Heuristica: blanco si el
            // accent es oscuro, negro si es claro.
            var accentLuma = (accentColor.R * 0.299 + accentColor.G * 0.587 + accentColor.B * 0.114);
            var onAccent = accentLuma < 140 ? Colors.White : Color.FromRgb(0x11, 0x11, 0x11);
            SetSemantic("Text.OnAccent.Brush", onAccent);

            // Subtle: derivado del border con alpha. Strong: el border crudo.
            var subtleBorder = Color.FromArgb(0x66, borderColor.R, borderColor.G, borderColor.B);
            SetSemantic("Border.Subtle.Brush", subtleBorder);
            SetSemantic("Border.Strong.Brush", borderColor);

            SetSemantic("Danger.Brush", errorColor);
            SetSemantic("Accent.Brush", accentColor);
        }

        private static void EnsureThemeComplete(ResourceDictionary theme)
        {
            // Ensure basic color keys exist; if missing, derive from PrimaryColor or defaults
            Color primary = Colors.CadetBlue;
            if (theme.Contains("PrimaryColor") && theme["PrimaryColor"] is Color pc) primary = pc;

            foreach (var key in RequiredColorKeys)
            {
                if (!theme.Contains(key))
                {
                    // Fallback rules
                    if (key == "WindowBackgroundColor") theme[key] = Color.FromRgb(18,18,18);
                    else if (key == "PanelBackgroundColor") theme[key] = Color.FromRgb(32,32,32);
                    else if (key == "TextColor") theme[key] = Color.FromRgb(255,255,255);
                    else if (key == "SecondaryTextColor") theme[key] = Color.FromRgb(170,170,170);
                    else if (key == "DisabledTextColor") theme[key] = Color.FromRgb(120,120,120);
                    else if (key == "AccentColor") theme[key] = primary;
                    else if (key == "SecondaryColor") theme[key] = primary;
                    else theme[key] = primary;
                }
            }

            // Re-generate brushes for any colors we added
            CreateBrushesFromColors(theme);

            // Accessibility: ensure sufficient contrast between text and window background.
            // If contrast is too low, pick a fallback (black or white) that provides better contrast.
            try
            {
                if (theme.Contains("WindowBackgroundColor") && theme.Contains("TextColor"))
                {
                    var wb = theme["WindowBackgroundColor"] as Color? ?? Colors.Black;
                    var tc = theme["TextColor"] as Color? ?? Colors.White;
                    double cr = ContrastRatio(wb, tc);
                    const double minContrast = 4.5; // WCAG AA for normal text
                    if (cr < minContrast)
                    {
                        // Choose black or white whichever gives better contrast against the background
                        var blackContrast = ContrastRatio(wb, Colors.Black);
                        var whiteContrast = ContrastRatio(wb, Colors.White);
                        var better = blackContrast >= whiteContrast ? Colors.Black : Colors.White;
                        theme["TextColor"] = better;
                        // Secondary/disabled text should also be adjusted to be a readable fraction of main text
                        theme["SecondaryTextColor"] = MakeRelativeShade((Color)theme["TextColor"], 0.65);
                        theme["DisabledTextColor"] = MakeRelativeShade((Color)theme["TextColor"], 0.4);
                        // Recreate brushes for the adjusted colors
                        CreateBrushesFromColors(theme);
                    }
                }
            }
            catch { }
        }

        // Compute relative luminance per WCAG
        private static double LinearizeChannel(byte c)
        {
            double v = c / 255.0;
            return (v <= 0.03928) ? (v / 12.92) : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        private static double RelativeLuminance(Color color)
        {
            return 0.2126 * LinearizeChannel(color.R) + 0.7152 * LinearizeChannel(color.G) + 0.0722 * LinearizeChannel(color.B);
        }

        private static double ContrastRatio(Color a, Color b)
        {
            var la = RelativeLuminance(a) + 0.05;
            var lb = RelativeLuminance(b) + 0.05;
            return la > lb ? la / lb : lb / la;
        }

        // Make a shade of the given base color by interpolating towards black/white to reach a readable tone
        private static Color MakeRelativeShade(Color baseColor, double factor)
        {
            // factor 1.0 => baseColor, factor 0.0 => black
            byte R = (byte)Math.Round(baseColor.R * factor);
            byte G = (byte)Math.Round(baseColor.G * factor);
            byte B = (byte)Math.Round(baseColor.B * factor);
            return Color.FromRgb(R, G, B);
        }

        /// <summary>
        /// Guarda el tema actual usando el nuevo sistema de persistencia v3.0
        /// </summary>
        public static void SaveCurrentTheme()
        {
            try
            {
                string themeName = CurrentTheme.ToString();
                var task = ComicReader.Services.PersistenceIntegrator.Instance.ChangeThemeAsync(themeName);
                task.Wait();
                
                ComicReader.Utils.ModernLogger.Info($"✓ Tema guardado con v3.0: {themeName}");
            }
            catch (Exception ex)
            {
                Logger.Log($"✗ Excepción guardando tema: {ex.Message}");
                ComicReader.Utils.DevLogger.Error($"Stack: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Carga el tema guardado usando el nuevo sistema de persistencia v3.0
        /// </summary>
        public static void LoadSavedTheme()
        {
            try
            {
                var config = ComicReader.Services.PersistenceIntegrator.Instance.GetThemeConfiguration();
                string savedThemeName = config?.CurrentTheme;
                
                if (!string.IsNullOrWhiteSpace(savedThemeName) && 
                    Enum.TryParse<ThemeMode>(savedThemeName, out var savedTheme))
                {
                    ComicReader.Utils.ModernLogger.Info($"📖 Cargando tema con v3.0: {savedTheme}");
                    CurrentTheme = savedTheme;
                }
                else
                {
                    ComicReader.Utils.ModernLogger.Info($"⚠ No se encontró tema válido, usando: {CurrentTheme}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"✗ Error cargando tema guardado: {ex.Message}");
                ComicReader.Utils.DevLogger.Error($"Stack: {ex.StackTrace}");
            }
        }

        public static List<ThemeInfo> GetAvailableThemes()
        {
            return new List<ThemeInfo>
            {
                new ThemeInfo(ThemeMode.Light, "Claro", "Tema claro y limpio para lectura diurna"),
                new ThemeInfo(ThemeMode.Dark, "Oscuro", "Tema oscuro para lectura nocturna"),
                new ThemeInfo(ThemeMode.Comic, "Cómic (Clásico)", "Tema colorido inspirado en cómics clásicos"),
                new ThemeInfo(ThemeMode.Sepia, "Sepia", "Tema vintage con tonos sepia"),
                new ThemeInfo(ThemeMode.HighContrast, "Alto Contraste", "Tema de alto contraste para accesibilidad"),

                // Marvel-inspired
                new ThemeInfo(ThemeMode.StarkRed, "Stark (Rojo)", "Paleta energizada, primarios rojos y metálicos"),
                new ThemeInfo(ThemeMode.PatriotBlue, "Patriot (Azul)", "Azules heroicos con acentos patrióticos"),
                new ThemeInfo(ThemeMode.ArañaRoja, "Araña Roja", "Tonos rojos brillantes para cómics de acción"),
                new ThemeInfo(ThemeMode.GammaGreen, "Gamma (Verde)", "Verdes intensos inspirados en fuerza bruta"),
                new ThemeInfo(ThemeMode.AsgardGold, "Asgard (Dorado)", "Dorados y púrpuras, estilo mitológico"),

                // DC-inspired
                new ThemeInfo(ThemeMode.BatNight, "Bat (Noche)", "Negros y azules profundos, estilo nocturno"),
                new ThemeInfo(ThemeMode.KryptonBlue, "Krypton (Azul)", "Azules brillantes con acentos solares"),
                new ThemeInfo(ThemeMode.MetroNeon, "Metro Neon", "Neones urbanos y contrastes intensos"),
                new ThemeInfo(ThemeMode.AmazonEmerald, "Amazon (Esmeralda)", "Verdes ricos y naturales"),
                new ThemeInfo(ThemeMode.OracleGray, "Oracle (Gris)", "Paleta sobria y profesional en grises"),

                // Manga styles
                new ThemeInfo(ThemeMode.ShonenBurst, "Shōnen (Explosivo)", "Colores vivos y contrastes para shōnen"),
                new ThemeInfo(ThemeMode.ShojoBloom, "Shōjo (Floral)", "Paleta suave y romántica"),
                new ThemeInfo(ThemeMode.SeinenNoir, "Seinen (Noir)", "Tonos oscuros y sobrios para seinen"),
                new ThemeInfo(ThemeMode.GekigaSepia, "Gekiga (Sepia)", "Estética madura en tonos sepia"),
                new ThemeInfo(ThemeMode.MangaInk, "Manga (Tinta)", "Blanco y negro, alto contraste tipo tinta"),

                // Classic ages & pop
                new ThemeInfo(ThemeMode.GoldenAge, "Edad Dorada", "Colores cálidos y nostálgicos"),
                new ThemeInfo(ThemeMode.SilverAge, "Edad de Plata", "Tonos suaves y clásicos de la era de plata"),
                new ThemeInfo(ThemeMode.BronzeAge, "Edad de Bronce", "Paleta terrosa con acentos retro"),
                new ThemeInfo(ThemeMode.PopArt, "Pop Art", "Colores saturados y contrastes pop"),
                new ThemeInfo(ThemeMode.Pulps, "Pulps", "Tonos oscuros y dramaticos estilo pulp"),

                // Retro / modern hybrids
                new ThemeInfo(ThemeMode.NoirStrip, "Noir Strip", "Monocromo elegante estilo tiras noir"),
                new ThemeInfo(ThemeMode.PastelRetro, "Pastel Retro", "Colores pastel inspirados en los 70/80"),
                new ThemeInfo(ThemeMode.NeonCyber, "Neon Cyber", "Neones brillantes y contrastes digitales"),
                new ThemeInfo(ThemeMode.Vaporwave, "Vaporwave", "Aesthetic vaporwave: magentas y cyans"),
                new ThemeInfo(ThemeMode.Retro80s, "Retro 80s", "Colores brillantes y gradients estilo 80s"),

                // Misc
                new ThemeInfo(ThemeMode.ComicPop, "Comic Pop", "Paleta alegre y saturada para cómics pop"),
                new ThemeInfo(ThemeMode.VintagePaper, "Papel Vintage", "Tonadas de papel envejecido y cálidos"),
                new ThemeInfo(ThemeMode.CelShade, "Cel Shading", "Colores planos y contrastes tipo cel"),
                new ThemeInfo(ThemeMode.CartoonBright, "Cartoon Brillante", "Colores brillantes y amigables"),
                new ThemeInfo(ThemeMode.MonochromeHighContrast, "Monocromo Alto Contraste", "Blanco y negro con máxima legibilidad"),
                new ThemeInfo(ThemeMode.PastelGentle, "Pastel Suave", "Tonos suaves y relajados para lectura tranquila"),
                new ThemeInfo(ThemeMode.TestHotPink, "⚠️ TEST ROSA", "Tema de prueba para verificar persistencia visual"),
                new ThemeInfo(ThemeMode.PercysLibrary, "Percy's Library", "Tema exclusivo con los colores oficiales de la aplicación: fondo azul profundo y acentos brand.")
            };
        }

        /// <summary>
        /// Devuelve una copia del ResourceDictionary correspondiente al tema solicitado.
        /// Útil para aplicar un tema a un control/ventana sin tocar los recursos globales de la aplicación.
        /// </summary>
        public static ResourceDictionary GetThemeDictionary(ThemeMode mode)
        {
            if (!_themes.TryGetValue(mode, out var src)) return null;
            try
            {
                var copy = new ResourceDictionary();
                foreach (var key in src.Keys)
                {
                    // Try to copy the value. If it's a Freezable (Brush, Color boxed), it's safe to reuse.
                    var val = src[key];
                    copy[key] = val;
                }
                return copy;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Validate that all loaded themes contain the required keys and brushes.
        /// Returns an empty list on success, or a list of human-readable error messages.
        /// </summary>
        public static List<string> ValidateAllThemes()
        {
            var issues = new List<string>();
            try
            {
                foreach (var kv in _themes)
                {
                    var mode = kv.Key;
                    var dict = kv.Value;
                    if (dict == null)
                    {
                        issues.Add($"Theme {mode} has no ResourceDictionary.");
                        continue;
                    }

                    // Check required color keys
                    foreach (var key in RequiredColorKeys)
                    {
                        if (!dict.Contains(key)) issues.Add($"Theme {mode}: missing color key {key}.");
                        else
                        {
                            var val = dict[key];
                            if (!(val is Color)) issues.Add($"Theme {mode}: key {key} is not a Color.");
                        }
                    }

                    // Check that brushes exist for each color
                    foreach (var key in RequiredColorKeys)
                    {
                        var brushKey = key.Replace("Color", "Brush");
                        if (!dict.Contains(brushKey)) issues.Add($"Theme {mode}: missing brush {brushKey}.");
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add("Exception while validating themes: " + ex.Message);
            }
            return issues;
        }
    }

    public class ThemeInfo
    {
        public ThemeMode Mode { get; }
        public string Name { get; }
        public string Description { get; }

        public ThemeInfo(ThemeMode mode, string name, string description)
        {
            Mode = mode;
            Name = name;
            Description = description;
        }
    }
}