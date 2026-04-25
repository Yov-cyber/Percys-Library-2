using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
// using System.Windows.Input; // ya está incluido arriba
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.ComponentModel;
using Microsoft.Win32;
using ComicReader.Views;
using ComicReader.Services;
using ComicReader.Models;
using ComicReader.ViewModels;
using System.Windows.Input;
using ComicReader.Core.Abstractions;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;
using ComicReader.Utils;
// using System.Threading; // duplicado eliminado

namespace ComicReader
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
    // Usar el nuevo loader optimizado
    private OptimizedComicPageLoader _comicLoader = new OptimizedComicPageLoader();
    private int _currentPageIndex;
    private HomeView _homeView; // Se inicializa tras cargar Settings
        private Image _currentComicImage;
    private ScrollViewer _readerScrollViewer;
    private Grid _readerCenterGrid;
    private bool _isPanning = false;
    private System.Windows.Point _panStartPoint;
    private double _panStartVerticalOffset;
    private double _panStartHorizontalOffset;
    private double _zoomFactor = 1.0;
        private bool _isComicOpen = false;
    private object _currentView;
        private bool _isNightMode = false;
        private bool _isReadingMode = false;
        private bool _thumbnailsVisible = false;
    private double _rotationAngle = 0;
    private IReadingStatsService _stats => ComicReader.Core.Services.ServiceLocator.TryGet<IReadingStatsService>();
        // Vista de lectura continua mejorada (scroll + zoom)
        private Views.EnhancedContinuousComicView _continuousView;
    // Secuencia para cancelar cargas obsoletas
    private long _pageLoadSeq = 0;
    // Secuencia independiente para miniaturas (no se invalida al cambiar de página)
    private long _thumbLoadSeq = 0;
    // Cancellation token source for background tasks launched by the window
    private readonly CancellationTokenSource _ctsWindow = new CancellationTokenSource();
    // Track background tasks launched by the window so we can wait for them on close
    private readonly System.Collections.Generic.List<Task> _backgroundTasks = new System.Collections.Generic.List<Task>();
    private readonly object _bgLock = new object();
    // Guard para evitar reentrada en el flujo de cierre
    private bool _isShuttingDown = false;
    // Placeholder ligero y congelado para reacción inmediata
    private static System.Windows.Media.Imaging.BitmapImage _frozen1x1Placeholder;

    private static System.Windows.Media.Imaging.BitmapImage GetFrozen1x1Placeholder()
    {
        if (_frozen1x1Placeholder != null) return _frozen1x1Placeholder;
        try
        {
            // Crear un RenderTargetBitmap 1x1 transparente y convertirlo a BitmapImage
            var rtb = new RenderTargetBitmap(1, 1, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Transparent background: no draw needed, but ensure the RenderTargetBitmap has alpha
            }
            rtb.Render(dv);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                ms.Position = 0;
                var bi = new System.Windows.Media.Imaging.BitmapImage();
                bi.BeginInit();
                bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                _frozen1x1Placeholder = bi;
                return _frozen1x1Placeholder;
            }
        }
        catch
        {
            return null;
        }
    }
    // Estado de pantalla completa inmersiva y overlay
    private bool _isImmersive = false;
    // Guardado de estado de ventana para inmersivo
    private double _savedLeft = double.NaN;
    private double _savedTop = double.NaN;
    private double _savedWidth = double.NaN;
    private double _savedHeight = double.NaN;
    private double _savedOverlayOpacity = 0.96;
    private bool _savedOverlayHit = true;
    // Estado overlay (modo inmersivo usa _savedOverlayOpacity/_savedOverlayHit)
    private WindowStyle _savedWindowStyle = WindowStyle.SingleBorderWindow;
    private ResizeMode _savedResizeMode = ResizeMode.CanResize;
    private WindowState _savedWindowState = WindowState.Normal;
    private Visibility _savedTitleBarVisibility = Visibility.Visible;
    private Visibility _savedTopBarVisibility = Visibility.Visible;
    private Visibility _savedThumbPanelVisibility = Visibility.Collapsed;
    private GridLength _savedThumbColWidth = new GridLength(0);
    private System.Windows.Media.Brush _savedBackgroundBrush = null;
    private bool _savedTopmost = false;
    private ScrollBarVisibility _savedVerticalScrollBarVisibility = ScrollBarVisibility.Auto;
    private ScrollBarVisibility _savedHorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
    private System.Windows.Media.BitmapScalingMode _savedImageScalingMode = System.Windows.Media.BitmapScalingMode.Unspecified;
    private bool _immersiveTransitionBusy = false;
    private bool _savedThumbColWidthSet = false;
    // loader metrics HUD removed
    // Evitar recursión al sincronizar selección del panel de miniaturas
    private bool _suppressThumbListSelectionChange = false;
    // Dirección de la última navegación: -1=prev, 1=next, 0=neutra
    private int _lastNavDirection = 0;

        // Variables para guardar el estado de la ventana cuando está en modo Normal
        private double _normalWidth = 1000;
        private double _normalHeight = 700;
        private double _normalLeft = -1;
        private double _normalTop = -1;
        private bool _windowStateInitialized = false;

        // Propiedades públicas para acceso desde Views
        public IComicPageLoader ComicLoader => _comicLoader;
        public int CurrentPageIndex => _currentPageIndex;
    // Compatibilidad: algunas rutas del diseñador buscan 'zoomLevel'
    public double zoomLevel => _zoomFactor;

        public event PropertyChangedEventHandler PropertyChanged;

        public object CurrentView
        {
            get => _currentView;
            set
            {
                _currentView = value;
                OnPropertyChanged(nameof(CurrentView));
                OnPropertyChanged(nameof(IsComicViewActive));
                OnPropertyChanged(nameof(IsReaderViewActive));
                OnPropertyChanged(nameof(IsHomeViewActive));
                OnPropertyChanged(nameof(IsSettingsViewActive));
                // Animación de transición breve al cambiar la vista principal
                try { AnimateMainContentChange(); } catch { }
            }
        }

        public bool IsComicViewActive => _isComicOpen;
        public bool IsReaderViewActive => CurrentView == _readerScrollViewer || CurrentView == _continuousView || _isComicOpen;
        public bool IsHomeViewActive => CurrentView == _homeView;
        public bool IsSettingsViewActive =>
            // Devuelve true si actualmente la ventana de Settings está abierta
            (System.Windows.Application.Current != null && System.Windows.Application.Current.Windows.OfType<Views.SettingsWindow>().Any());

        public MainWindow()
        {
            // Carga el XAML de la ventana. Sin esta llamada, la UI queda en blanco.
            try
            {
                var mi = this.GetType().GetMethod("InitializeComponent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                mi?.Invoke(this, null);
            }
            catch { }

            // Arrancar animaciones visuales de cabecera (sorpresa sutil)
            this.Loaded += (s, e) => 
            {
                StartHeaderShimmer();
                InitializePremiumServices();
            };

            // Inicialización adicional específica de la app
            InitializeComponents();
            DataContext = this;
            // Suscribir eventos removidos del XAML
            this.Drop += Window_Drop;
            this.KeyDown += MainWindow_KeyDown;
            // Interceptar teclas antes de que controles internos (p.ej. ScrollViewer) las consuman
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            this.AllowDrop = true;
            // Inicializar vista continua mejorada con carga instantánea y zoom
            _continuousView = new Views.EnhancedContinuousComicView();
            _continuousView.CurrentPageChanged += (idx) =>
            {
                _currentPageIndex = idx;
                try { if (this.FindName("PageIndicator") is TextBlock pi) pi.Text = $"Página {idx + 1} de {_comicLoader.PageCount}"; } catch { }
                try
                {
                    _stats?.RecordPageViewed(idx + 1);
                    SettingsManager.Settings.LastOpenedFilePath = _comicLoader.FilePath;
                    SettingsManager.Settings.LastOpenedPage = idx;
                    SettingsManager.SaveSettings();
                    // Actualizar progreso en el nuevo servicio de "Seguir leyendo"
                    var oneBased = idx + 1;
                    var pageCount = _comicLoader?.PageCount ?? 0;
                    ComicReader.Services.ContinueReadingService.Instance.UpsertProgress(_comicLoader?.FilePath, oneBased, pageCount);
                    if (pageCount > 0 && oneBased >= pageCount)
                    {
                        // UpsertProgress moves the item to Completed; refresh UI but do not remove it explicitly here.
                        _homeView?.RefreshRecent();
                    }
                }
                catch { }
            };
            // Escuchar cambios de configuración y aplicarlos en caliente
            if (SettingsManager.Settings is INotifyPropertyChanged npc)
            {
                npc.PropertyChanged += (_, __) =>
                {
                    try { this.Dispatcher.BeginInvoke(new Action(ApplySettingsRuntime)); } catch { }
                };
            }
            
            // Aplicar configuraciones del sistema v3.0 que requieren UI
            try
            {
                // Aplicar tema guardado
                ComicReader.Themes.ThemeManager.LoadSavedTheme();
                
                // Aplicar configuración UI desde PersistenceIntegrator
                var config = ComicReader.Services.Persistence.ConfigurationManager.Instance.GetConfiguration();
                if (config?.UI != null)
                {
                    System.Windows.Application.Current.Resources["DefaultFontSize"] = (double)config.UI.FontSize;
                    System.Windows.Application.Current.Resources["DefaultFontFamily"] = config.UI.FontFamily;
                }
            }
            catch (Exception ex)
            {
                ComicReader.Utils.ModernLogger.Warning($"No se pudo aplicar configuración UI: {ex.Message}");
            }
            
            // Configurar ventana inicial
            ConfigureInitialWindowState();
            this.Closing += MainWindow_Closing;
            // En modo DEBUG, abrir la Configuración automáticamente una vez para pruebas rápidas
            // (DEBUG auto-open removed)
            // Loader HUD removed; no timer initialized.
            // Ajustar límites al área de trabajo cuando cambie el estado
            this.StateChanged += (s, e) => ApplyWorkAreaBounds();
            this.Loaded += (s, e) => ApplyWorkAreaBounds();
        }

        // Asegurar que al maximizar no se cubra la barra de tareas (usar WorkArea)
        // Se aplica en Loaded/StateChanged para no duplicar overrides existentes.

        private void ApplyWorkAreaBounds()
        {
            try
            {
                // Si estamos en modo inmersivo, no limitar; ahí sí puede cubrir la barra de tareas
                if (_isImmersive) return;
                // Cuando la ventana está maximizada queremos cubrir TODO el monitor (incluida la barra de tareas)
                // y no usar WorkArea. Esto permite que la aplicación ocupe la pantalla completa cuando el usuario
                // presiona maximizar (tal como pidió el usuario).
                var wa = SystemParameters.WorkArea;
                if (this.WindowState == WindowState.Maximized)
                {
                    try
                    {
                        var m = GetCurrentMonitorBounds();
                        this.MaxHeight = m.Height;
                        this.MaxWidth = m.Width;
                        this.Top = m.Top;
                        this.Left = m.Left;
                        // ❌ REMOVIDO: No usar Topmost para permitir que otras apps se abran encima
                        // this.Topmost = true;
                    }
                    catch
                    {
                        // Fallback a WorkArea si falla la consulta del monitor
                        this.MaxHeight = wa.Height;
                        this.MaxWidth = wa.Width;
                        this.Top = wa.Top;
                        this.Left = wa.Left;
                    }
                }
                else
                {
                    // Restablecer límites cuando no está maximizada
                    this.MaxHeight = double.PositiveInfinity;
                    this.MaxWidth = double.PositiveInfinity;
                    // Ya no necesitamos esto porque nunca establecemos Topmost
                    // this.Topmost = false;
                }
            }
            catch { }
        }

        // P/Invoke para obtener el rectángulo completo del monitor (incluye la región ocupada por la barra de tareas)
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left; public int top; public int right; public int bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private System.Windows.Rect GetCurrentMonitorBounds()
        {
            var helper = new WindowInteropHelper(this);
            IntPtr h = helper.Handle;
            const uint MONITOR_DEFAULTTONEAREST = 2;
            var hMon = MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO();
            info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (GetMonitorInfo(hMon, ref info))
            {
                var r = info.rcMonitor;
                return new System.Windows.Rect(r.left, r.top, r.right - r.left, r.bottom - r.top);
            }
            // Fallback: usar parámetros de pantalla principal
            return new System.Windows.Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        }

        // Crea y aplica un LinearGradientBrush animado en la cabecera para dar una sensación viva.
        private void StartHeaderShimmer()
        {
            try
            {
                var border = this.FindName("CustomTitleBar") as System.Windows.Controls.Border;
                if (border == null) return;

                // Crear un gradiente animable
                var g = new LinearGradientBrush();
                g.StartPoint = new System.Windows.Point(0, 0);
                g.EndPoint = new System.Windows.Point(1, 0);
                var gs1 = new GradientStop((System.Windows.Media.Color)Application.Current.Resources["Color.Primary.Blue"], 0.0);
                var gs2 = new GradientStop(System.Windows.Media.Colors.Transparent, 0.5);
                var gs3 = new GradientStop((System.Windows.Media.Color)Application.Current.Resources["Color.Primary.Purple"], 1.0);
                g.GradientStops.Add(gs1);
                g.GradientStops.Add(gs2);
                g.GradientStops.Add(gs3);

                border.Background = g;

                // Animar el offset de los gradient stops para crear un suave barrido
                var anim1 = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = -0.3,
                    To = 1.3,
                    Duration = new Duration(TimeSpan.FromSeconds(6)),
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    AutoReverse = true,
                    EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                var anim2 = anim1.Clone();
                anim2.BeginTime = TimeSpan.FromSeconds(1.2);

                gs1.BeginAnimation(GradientStop.OffsetProperty, anim1);
                gs3.BeginAnimation(GradientStop.OffsetProperty, anim2);
            }
            catch { }
        }

        private void AnimateMainContentChange()
        {
            try
            {
                var content = this.FindName("MainContentArea") as System.Windows.Controls.ContentControl;
                if (content == null) return;
                // Aplicar una animación de fundido rápido
                var fade = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(220)),
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                content.Opacity = 0.0;
                content.BeginAnimation(System.Windows.UIElement.OpacityProperty, fade);
            }
            catch { }
        }

        private async void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            // If shutdown already in progress, allow the close to continue.
            if (_isShuttingDown) return;

            // Cancel the close for now and perform graceful shutdown work.
            e.Cancel = true;
            _isShuttingDown = true;

            try
            {
                await HandleWindowClosingAsync().ConfigureAwait(false);
            }
            catch { }

            // After cleanup, invoke Close again on UI thread to finish shutdown.
            try
            {
                this.Dispatcher.Invoke(() => {
                    try { this.Close(); } catch { System.Windows.Application.Current?.Shutdown(); }
                });
            }
            catch { }
        }

        private async System.Threading.Tasks.Task HandleWindowClosingAsync()
        {
            try { _ctsWindow?.Cancel(); } catch { }

            // Guardar configuración de ventana con v3.0
            try
            {
                await this.Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await ComicReader.Services.PersistenceIntegrator.Instance.UpdateWindowConfigurationAsync(
                            width: this.ActualWidth,
                            height: this.ActualHeight,
                            left: this.Left,
                            top: this.Top,
                            isMaximized: this.WindowState == WindowState.Maximized
                        );
                        ComicReader.Utils.ModernLogger.Info("✓ Configuración de ventana guardada");
                    }
                    catch (Exception ex)
                    {
                        ComicReader.Utils.ModernLogger.Error($"Error guardando ventana: {ex.Message}");
                    }
                });
            }
            catch { }

            // Request a save (this schedules the debounced save and records the task)
            try { SettingsManager.SaveSettings(); } catch { }

            try
            {
                // Wait for pending settings save (bounded)
                try
                {
                    var cts = new CancellationTokenSource(3000);
                    await SettingsManager.FlushPendingSavesAsync(cts.Token).ConfigureAwait(false);
                }
                catch { /* timeout or failure — proceed */ }

                // Wait for window-tracked background tasks with a timeout
                Task[] tasksCopy;
                lock (_bgLock) { tasksCopy = _backgroundTasks.ToArray(); }
                if (tasksCopy != null && tasksCopy.Length > 0)
                {
                    try
                    {
                        var timeout = System.Threading.Tasks.Task.Delay(3000);
                        var all = System.Threading.Tasks.Task.WhenAll(tasksCopy);
                        var finished = await System.Threading.Tasks.Task.WhenAny(all, timeout).ConfigureAwait(false);
                        try { await all.ConfigureAwait(false); } catch { }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void TrackBackgroundTask(Task t)
        {
            if (t == null) return;
            lock (_bgLock) { _backgroundTasks.Add(t); }
            t.ContinueWith(_ => { try { lock (_bgLock) { _backgroundTasks.Remove(t); } } catch { } }, System.Threading.Tasks.TaskScheduler.Default);
        }

        // Actualiza los contenidos (texto/icono) de los botones de navegación según el modo continuo
        private void UpdateNavButtonsForContinuousMode(bool continuous)
        {
            try
            {
                var pb = this.FindName("PrevButton") as System.Windows.Controls.Button;
                var nb = this.FindName("NextButton") as System.Windows.Controls.Button;
                if (pb != null && nb != null)
                {
                    if (continuous)
                    {
                        // Colocar Paths vectoriales para flecha arriba/abajo
                        var up = new System.Windows.Shapes.Path
                        {
                            Data = System.Windows.Media.Geometry.Parse("M6,14 L12,8 L18,14"),
                            Stroke = System.Windows.Media.Brushes.White,
                            StrokeThickness = 2,
                            StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
                            StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
                            Width = 14,
                            Height = 14,
                            Stretch = System.Windows.Media.Stretch.Uniform
                        };
                        var down = new System.Windows.Shapes.Path
                        {
                            Data = System.Windows.Media.Geometry.Parse("M6,10 L12,16 L18,10"),
                            Stroke = System.Windows.Media.Brushes.White,
                            StrokeThickness = 2,
                            StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
                            StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
                            Width = 14,
                            Height = 14,
                            Stretch = System.Windows.Media.Stretch.Uniform
                        };
                        pb.Content = up;
                        pb.ToolTip = "Subir";
                        nb.Content = down;
                        nb.ToolTip = "Bajar";
                    }
                    else
                    {
                        // Restaurar Paths laterales
                        var left = new System.Windows.Shapes.Path
                        {
                            Data = System.Windows.Media.Geometry.Parse("M12,3 L4,12 L12,21"),
                            Stroke = System.Windows.Media.Brushes.White,
                            StrokeThickness = 2,
                            StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
                            StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
                            Width = 14,
                            Height = 14,
                            Stretch = System.Windows.Media.Stretch.Uniform
                        };
                        var right = new System.Windows.Shapes.Path
                        {
                            Data = System.Windows.Media.Geometry.Parse("M4,3 L12,12 L4,21"),
                            Stroke = System.Windows.Media.Brushes.White,
                            StrokeThickness = 2,
                            StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
                            StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
                            Width = 14,
                            Height = 14,
                            Stretch = System.Windows.Media.Stretch.Uniform
                        };
                        pb.Content = left;
                        pb.ToolTip = "Anterior";
                        nb.Content = right;
                        nb.ToolTip = "Siguiente";
                    }
                }
            }
            catch { }
        }

        // Animación suave hacia un offset vertical objetivo en un ScrollViewer
        private void SmoothScrollTo(System.Windows.Controls.ScrollViewer sv, double targetOffset)
        {
            try
            {
                if (sv == null) return;
                // Usar DoubleAnimation para animar un DependencyProperty proxy
                var anim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = sv.VerticalOffset,
                    To = targetOffset,
                    Duration = new Duration(TimeSpan.FromMilliseconds(240)),
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };

                var storyboard = new System.Windows.Media.Animation.Storyboard();
                storyboard.Children.Add(anim);

                // Crear un DependencyObject temporal para enlazar la animación
                var proxy = new DependencyObject();
                var prop = System.Windows.DependencyProperty.RegisterAttached("ScrollAnim", typeof(double), typeof(MainWindow), new PropertyMetadata(0.0, (d, e) =>
                {
                    try
                    {
                        var value = (double)e.NewValue;
                        sv.ScrollToVerticalOffset(value);
                    }
                    catch { }
                }));

                System.Windows.Media.Animation.Storyboard.SetTarget(anim, proxy);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(anim, new PropertyPath(prop));
                storyboard.Begin();
            }
            catch { }
        }

        // Prefetch and Clear caches UI handlers removed (buttons deleted from XAML).

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Asegurar que la ventana tenga el tamaño correcto al inicializarse
            if (this.WindowState != WindowState.Maximized)
            {
                this.Width = _normalWidth;
                this.Height = _normalHeight;
            }
        }

        private void ConfigureInitialWindowState()
        {
            // Configurar tamaño y posición inicial de manera más agresiva
            this.WindowStartupLocation = WindowStartupLocation.Manual;
            this.MinWidth = 800;
            this.MinHeight = 600;
            
            // Cargar configuración de ventana desde v3.0
            try
            {
                var windowConfig = ComicReader.Services.PersistenceIntegrator.Instance.GetWindowConfiguration();
                if (windowConfig != null)
                {
                    // Tamaño normal recordado
                    _normalWidth = Math.Max(800, windowConfig.Width);
                    _normalHeight = Math.Max(600, windowConfig.Height);
                    
                    // Aplicar tamaño y posición
                    this.Width = _normalWidth;
                    this.Height = _normalHeight;
                    this.Left = windowConfig.Left;
                    this.Top = windowConfig.Top;
                    
                    // Estado recordado
                    if (windowConfig.IsMaximized)
                    {
                        // Diferir a Loaded para evitar parpadeos
                        this.Loaded += (_, __) =>
                        {
                            try { this.WindowState = WindowState.Maximized; } catch { }
                        };
                    }
                    
                    ComicReader.Utils.ModernLogger.Info($"✓ Configuración de ventana cargada: {_normalWidth}x{_normalHeight}");
                }
                else
                {
                    // Fallback si no hay configuración
                    this.Width = _normalWidth;
                    this.Height = _normalHeight;
                    CenterWindowOnScreen();
                }
            }
            catch (Exception ex)
            {
                ComicReader.Utils.ModernLogger.Error($"Error cargando ventana: {ex.Message}");
                // Fallback
                this.Width = _normalWidth;
                this.Height = _normalHeight;
                CenterWindowOnScreen();
            }
            
            // Fallback adicional desde SettingsManager (compatibilidad temporal)
            var s = SettingsManager.Settings;
            if (s != null && this.Width == 1200) // Si no se cargó de v3.0
            {
                // Tamaño normal recordado
                _normalWidth = Math.Max(800, s.LastWindowWidth);
                _normalHeight = Math.Max(600, s.LastWindowHeight);
                // Aplicar tamaño inicialmente solo si no está maximizada
                this.Width = _normalWidth;
                this.Height = _normalHeight;
                // Estado recordado
                if (s.LastWindowState == WindowState.Maximized)
                {
                    // Diferir a Loaded para evitar parpadeos
                    this.Loaded += (_, __) =>
                    {
                        try { this.WindowState = WindowState.Maximized; } catch { }
                    };
                }
            }
            
            // Forzar el tamaño inmediatamente
            this.SizeToContent = SizeToContent.Manual;
            
            // Evento para inicialización después de cargar
            this.Loaded += (s, e) =>
            {
                // Forzar tamaño y posición después de cargar
                if (this.WindowState != WindowState.Maximized)
                {
                    this.Width = _normalWidth;
                    this.Height = _normalHeight;
                }
                
                // Guardar posición inicial después de que la ventana se muestre
                _normalLeft = this.Left;
                _normalTop = this.Top;
                _normalWidth = this.ActualWidth;
                _normalHeight = this.ActualHeight;
                _windowStateInitialized = true;
                
                System.Diagnostics.Debug.WriteLine($"Inicializado: L={_normalLeft}, T={_normalTop}, W={_normalWidth}, H={_normalHeight}");
            };
            
            // Solo rastrear cambios manuales del usuario (simplificado)
            this.LocationChanged += (s, e) =>
            {
                if (this.WindowState == WindowState.Normal && _windowStateInitialized && this.IsLoaded)
                {
                    _normalLeft = this.Left;
                    _normalTop = this.Top;
                    // Guardar en settings (persistirá al cerrar)
                    try
                    {
                        SettingsManager.Settings.LastWindowState = this.WindowState;
                        SettingsManager.Settings.LastWindowWidth = this.ActualWidth;
                        SettingsManager.Settings.LastWindowHeight = this.ActualHeight;
                    }
                    catch { }
                }
            };
            
            this.SizeChanged += (s, e) =>
            {
                if (this.WindowState == WindowState.Normal && _windowStateInitialized && this.IsLoaded)
                {
                    _normalWidth = this.ActualWidth;
                    _normalHeight = this.ActualHeight;
                    // Guardar en settings (persistirá al cerrar)
                    try
                    {
                        SettingsManager.Settings.LastWindowState = this.WindowState;
                        SettingsManager.Settings.LastWindowWidth = this.ActualWidth;
                        SettingsManager.Settings.LastWindowHeight = this.ActualHeight;
                    }
                    catch { }
                }
            };
        }

        private void InitializeComponents()
        {
            _currentPageIndex = 0;
            // Asegurar que los ajustes estén cargados antes de crear vistas que dependan de ellos
            SettingsManager.LoadSettings();

            // Crear vistas DESPUÉS de cargar Settings para que se suscriban a la instancia correcta
            _homeView = new HomeView();
            // Nuevo: no crear _settingsView aquí; usamos una ventana separada para la configuración

            Title = "Percy's Library";
            CurrentView = _homeView;
            // Inicio siempre en Home: no abrir automáticamente la última sesión
            this.Loaded += (s, e) =>
            {
                // Dejamos la posibilidad de mostrar un botón "Continuar última sesión" en Home en el futuro.
            };
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void ShowWelcomeScreen()
        {
            // Cancelar cargas/miniaturas pendientes
            try { Interlocked.Increment(ref _pageLoadSeq); } catch { }
            try { Interlocked.Increment(ref _thumbLoadSeq); } catch { }
            if (this.FindName("MainContentArea") is ContentControl content)
                content.Content = _homeView;
            CurrentView = _homeView;
            _isComicOpen = false;
            OnPropertyChanged(nameof(IsComicViewActive));
            // Asegurar que el panel de miniaturas no quede visible fuera del lector
            HideThumbnailsPanel();
            // Ocultar barra del lector fuera del modo lectura
            SetReaderTopBarVisible(false);
        }

        private void ShowHomeView()
        {
            // Cancelar cargas/miniaturas pendientes
            try { Interlocked.Increment(ref _pageLoadSeq); } catch { }
            try { Interlocked.Increment(ref _thumbLoadSeq); } catch { }
            if (this.FindName("MainContentArea") is ContentControl content)
                content.Content = _homeView;
            CurrentView = _homeView;
            _isComicOpen = false;
            OnPropertyChanged(nameof(IsComicViewActive));
            OnPropertyChanged(nameof(IsReaderViewActive));
            try { _currentComicImage?.ClearValue(Image.SourceProperty); } catch { }
            // Cerrar sesión de lectura cuando volvemos a inicio
            try { _stats?.EndSession(); } catch { }
            // Refrescar recientes al volver a inicio
            try { _homeView?.RefreshRecent(); } catch { }
            // Ocultar miniaturas si estaban activas
            HideThumbnailsPanel();
            // Ocultar barra del lector
            SetReaderTopBarVisible(false);
        }

        private void ShowSettingsView()
        {
            // Abrir la configuración en una ventana separada (no embebida)
            try
            {
                // Si ya existe una ventana de Settings abierta, llevarla al frente.
                var existing = System.Windows.Application.Current?.Windows
                    .OfType<Views.SettingsWindow>()
                    .FirstOrDefault();
                try { ComicReader.Utils.DevLogger.Debug("ShowSettingsView invoked. Existing SettingsWindow? " + (existing != null)); } catch { }
                // Debug message removed
                if (existing != null)
                {
                    try { existing.WindowState = System.Windows.WindowState.Normal; } catch { }
                    try { existing.Owner = this; } catch { }
                    try { existing.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner; } catch { }
                    try { existing.ShowInTaskbar = false; } catch { }
                    try { existing.Activate(); } catch { }
                    try { existing.Focus(); } catch { }
                }
                else
                {
                    var win = new Views.SettingsWindow();
                    try { ComicReader.Utils.DevLogger.Debug("Creating new SettingsWindow instance."); } catch { }
                    // Set owner so the settings window stays above the main window and is focused
                    try { win.Owner = this; } catch { }
                    try { win.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner; } catch { }
                    try { win.ShowInTaskbar = false; } catch { }
                    // Open as modal to ensure it appears above and receives focus. If non-modal behavior is desired
                    // in the future, switch to Show() and call Activate()/Focus() as needed.
                    try { win.ShowDialog(); } catch
                    {
                        // Fallback to non-modal show if dialog fails for any reason
                        try { win.Show(); win.Activate(); win.Focus(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                try { MessageBox.Show($"No se pudo abrir Configuración: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
            }
            finally
            {
                // Asegurar el mismo efecto sobre la UI principal que antes
                try { HideThumbnailsPanel(); } catch { }
                try { SetReaderTopBarVisible(false); } catch { }
            }
        }

        private void HideThumbnailsPanel()
        {
            try
            {
                var panel = this.FindName("ThumbPanel") as FrameworkElement;
                var col = this.FindName("ThumbCol") as System.Windows.Controls.ColumnDefinition;
                var list = this.FindName("ThumbList") as System.Windows.Controls.ListBox;
                if (panel != null) panel.Visibility = Visibility.Collapsed;
                if (col != null) col.Width = new GridLength(0);
                if (list != null)
                {
                    list.ItemsSource = null;
                    list.SelectedIndex = -1;
                }
                _thumbnailsVisible = false;
                // Invalida cargas de miniaturas en curso
                try { Interlocked.Increment(ref _thumbLoadSeq); } catch { }
            }
            catch { }
        }

        /// <summary>
        /// Refresca el panel de miniaturas con las páginas del cómic actualmente cargado.
        /// Usar cuando el panel ya estaba visible y se abre un nuevo archivo, para evitar que se muestren las miniaturas del cómic anterior.
        /// </summary>
        private void RefreshThumbnailPanelForCurrentComic()
        {
            try
            {
                if (!_isComicOpen || _comicLoader?.Pages == null) return;
                var list = this.FindName("ThumbList") as System.Windows.Controls.ListBox;
                if (list == null) return;

                // Forzar rebind: primero limpiar ItemsSource
                _suppressThumbListSelectionChange = true;
                try
                {
                    list.ItemsSource = null;
                    list.Items.Clear();
                    // Asignar nueva fuente y selección actual
                    list.ItemsSource = _comicLoader.Pages;
                    list.SelectedIndex = Math.Max(0, Math.Min(_currentPageIndex, _comicLoader.Pages.Count - 1));
                    try { list.Items.Refresh(); } catch { }
                    // Llevar a la vista la miniatura actual
                    list.ScrollIntoView(list.SelectedItem);
                }
                finally { _suppressThumbListSelectionChange = false; }

                // Limpiar miniaturas antiguas para evitar parpadeos de referencias cruzadas
                foreach (var p in _comicLoader.Pages)
                {
                    p.Thumbnail = null;
                }

                // Cargar en segundo plano las miniaturas del cómic activo con guardas de secuencia
                long startSeq = Interlocked.Increment(ref _thumbLoadSeq);
                var loaderRef = _comicLoader;
                var token = _ctsWindow.Token;
                var _thumbLoadTask = Task.Run(async () =>
                {
                    try
                    {
                        int count = loaderRef?.Pages?.Count ?? 0;
                        int maxDegree = Math.Max(2, Math.Min(Environment.ProcessorCount, 6));
                        using var gate = new System.Threading.SemaphoreSlim(maxDegree);
                        var tasks = Enumerable.Range(0, count).Select(async i =>
                        {
                            if (token.IsCancellationRequested) return;
                            await gate.WaitAsync(token).ConfigureAwait(false);
                            try
                            {
                                if (token.IsCancellationRequested) return;
                                var thumb = await loaderRef.GetPageThumbnailAsync(i, 180, 240, token).ConfigureAwait(false);
                                var idx = i;
                                this.Dispatcher.Invoke(() =>
                                {
                                    if (token.IsCancellationRequested) return;
                                    if (!ReferenceEquals(_comicLoader, loaderRef)) return;
                                    if (Interlocked.Read(ref _thumbLoadSeq) != startSeq) return;
                                    if (idx >= 0 && idx < _comicLoader.Pages.Count)
                                        _comicLoader.Pages[idx].Thumbnail = thumb;
                                });
                            }
                            finally { try { gate.Release(); } catch { } }
                        }).ToArray();
                        try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { }
                    }
                    catch { }
                }, token);
                TrackBackgroundTask(_thumbLoadTask);
            }
            catch { }
        }
        
        // Re-renderizar PDF actual con los nuevos ajustes (si aplica)
        public async void ReRenderCurrentPdfIfAny()
        {
            try
            {
                var path = _comicLoader?.FilePath;
                if (string.IsNullOrEmpty(path)) return;
                var ext = System.IO.Path.GetExtension(path)?.ToLowerInvariant();
                if (ext != ".pdf") return;
                // Guardar página actual para restaurar
                int page = _currentPageIndex;
                // Limpiar cache del loader y recargar
                await _comicLoader.LoadComicAsync(path);
                // Restaurar página si es válida
                if (page >= 0 && page < _comicLoader.Pages.Count)
                {
                    _currentPageIndex = page;
                }
                LoadCurrentPage();
                // Si está activa la vista continua, pedirle que recargue
                // TODO: Implementar con EnhancedContinuousComicView
                // try { _continuousView?.ViewModel?.RequestVisiblePagesMaterialization(); } catch { }
            }
            catch (Exception ex)
            {
                ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(
                    ex, 
                    "Re-renderizar PDF",
                    ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Notify
                );
            }
        }

        private void OpenSettingsDialog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowSettingsView();
            }
            catch (Exception ex)
            {
                ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(
                    ex, 
                    "Abrir configuración",
                    ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Notify
                );
            }
        }

        // -------------------------------------------------------------------
        // Navegacion principal (sidebar). Cada click marca su boton como activo
        // (Tag="active") y desactiva los demas. Biblioteca cambia el CurrentView a
        // _homeView; las otras secciones aun viven en ventanas modales (F6.1/F6.2
        // las convierten en vistas embebidas).
        // -------------------------------------------------------------------
        private void SetActiveNavButton(string activeName)
        {
            string[] all = { "NavLibraryButton", "NavCollectionsButton", "NavStatisticsButton", "NavAchievementsButton" };
            foreach (var n in all)
            {
                if (this.FindName(n) is System.Windows.Controls.Button b)
                    b.Tag = (n == activeName) ? "active" : null;
            }
        }

        private void NavLibrary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetActiveNavButton("NavLibraryButton");
                ShowHomeView();
            }
            catch { }
        }

        private void NavCollections_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetActiveNavButton("NavCollectionsButton");
                var w = new ComicReader.Views.FavoritesWindow { Owner = this };
                w.ShowDialog();
                SetActiveNavButton("NavLibraryButton");
            }
            catch { SetActiveNavButton("NavLibraryButton"); }
        }

        private void NavStatistics_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetActiveNavButton("NavStatisticsButton");
                var w = new ComicReader.Views.StatisticsWindow { Owner = this };
                w.ShowDialog();
                SetActiveNavButton("NavLibraryButton");
            }
            catch { SetActiveNavButton("NavLibraryButton"); }
        }

        private void NavAchievements_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetActiveNavButton("NavAchievementsButton");
                var w = new ComicReader.Views.AchievementsWindow { Owner = this };
                w.ShowDialog();
                SetActiveNavButton("NavLibraryButton");
            }
            catch { SetActiveNavButton("NavLibraryButton"); }
        }

        public async void ShowComicView()
        {
            bool useContinuous = SettingsManager.Settings?.EnableContinuousScroll == true;
            if (useContinuous)
            {
                await _continuousView.LoadComicAsync(_comicLoader);
                if (this.FindName("MainContentArea") is ContentControl content)
                    content.Content = _continuousView;
                CurrentView = _continuousView;
                // Actualizar aspecto de los botones de navegación (seguir habilitados para actuar como subir/bajar)
                UpdateNavButtonsForContinuousMode(true);
                try { if (this.FindName("PrevButton") is System.Windows.Controls.Button pb) pb.IsEnabled = true; } catch { }
                try { if (this.FindName("NextButton") is System.Windows.Controls.Button nb) nb.IsEnabled = true; } catch { }
            }
            else
            {
                if (_currentComicImage == null)
                {
                    _currentComicImage = new Image
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Stretch = System.Windows.Media.Stretch.Uniform
                    };
                    _currentComicImage.MouseLeftButtonDown += Image_MouseLeftButtonDown;
                    _currentComicImage.MouseLeftButtonUp += Image_MouseLeftButtonUp;
                    _currentComicImage.MouseMove += Image_MouseMove;
                    _currentComicImage.MouseEnter += Image_MouseEnter;
                    _currentComicImage.MouseLeave += Image_MouseLeave;
                    _readerCenterGrid = new Grid
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    _readerCenterGrid.Children.Add(_currentComicImage);
                    // Si se hace clic en el fondo (zonas en blanco), devolver el foco al lector
                    _readerCenterGrid.MouseDown += (s, e2) =>
                    {
                        try { _readerScrollViewer?.Focus(); Keyboard.Focus(_readerScrollViewer); } catch { }
                    };

                    _readerScrollViewer = new ScrollViewer
                    {
                        Content = _readerCenterGrid,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        CanContentScroll = false,
                        PanningMode = PanningMode.Both,
                        Focusable = true
                    };
                    _readerScrollViewer.PreviewMouseWheel += (s, eargs) =>
                    {
                        // Si hay contenido desplazable, deja que el ScrollViewer maneje la rueda
                        try
                        {
                            if (_readerScrollViewer != null && _readerScrollViewer.ScrollableHeight > 0.5)
                            {
                                // No marcar como handled para permitir el comportamiento por defecto del ScrollViewer
                                return;
                            }
                        }
                        catch { }
                    };
                    // Si el usuario hace clic en el área blanca del ScrollViewer, devolver foco al lector
                    _readerScrollViewer.PreviewMouseDown += (s, eargs) =>
                    {
                        try { _readerScrollViewer?.Focus(); Keyboard.Focus(_readerScrollViewer); } catch { }
                    };
                }
                if (this.FindName("MainContentArea") is ContentControl content)
                    content.Content = _readerScrollViewer;
                CurrentView = _readerScrollViewer;
                // Actualizar aspecto y estado de los botones de navegación
                UpdateNavButtonsForContinuousMode(false);
                try { if (this.FindName("PrevButton") is System.Windows.Controls.Button pb) pb.IsEnabled = true; } catch { }
                try { if (this.FindName("NextButton") is System.Windows.Controls.Button nb) nb.IsEnabled = true; } catch { }
            }
            // Mostrar barra del lector en modo lectura
            SetReaderTopBarVisible(true);
            _isComicOpen = true;
            OnPropertyChanged(nameof(IsComicViewActive));
            OnPropertyChanged(nameof(IsReaderViewActive));
            // Restaurar preferencia de miniaturas
            try
            {
                var wantThumbs = SettingsManager.Settings?.ThumbnailsVisible == true;
                if (wantThumbs)
                {
                    if (!_thumbnailsVisible)
                    {
                        ToggleThumbnails_Click(null, null);
                    }
                    else
                    {
                        // Ya está visible: refrescar la lista para este cómic
                        RefreshThumbnailPanelForCurrentComic();
                    }
                }
                else
                {
                    HideThumbnailsPanel();
                }
            }
            catch { }
            if (!useContinuous)
            {
                LoadCurrentPage();
            }

            // Aplicar ajuste por defecto después de cargar
            _ = this.Dispatcher.BeginInvoke(new Action(() =>
            {
                var mode = SettingsManager.Settings?.DefaultFitMode?.ToLowerInvariant();
                switch (mode)
                {
                    case "height":
                        ApplyFitToHeight();
                        break;
                    case "screen":
                        FitToScreen();
                        break;
                    case "original":
                        _zoomFactor = 1.0; ApplyZoomToImage(); UpdateZoomIndicator();
                        break;
                    case "width":
                    default:
                        FitToWidth();
                        break;
                }
                _isNightMode = SettingsManager.Settings?.IsNightMode == true;
                _isReadingMode = SettingsManager.Settings?.IsReadingMode == true;
                ApplyReadingModeEffects();
                EnsureAutoAdvanceBehavior();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void SetReaderTopBarVisible(bool visible)
        {
            try
            {
                if (this.FindName("ReaderTopBar") is FrameworkElement topBar)
                {
                    topBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch { }
        }

        private async void EnsureReaderScaffold()
        {
            bool useContinuous = SettingsManager.Settings?.EnableContinuousScroll == true;
            if (useContinuous)
            {
                await _continuousView.LoadComicAsync(_comicLoader);
                if (this.FindName("MainContentArea") is ContentControl content)
                    content.Content = _continuousView;
                CurrentView = _continuousView;
            }
            else
            {
                if (_currentComicImage == null)
                {
                    _currentComicImage = new Image
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Stretch = System.Windows.Media.Stretch.Uniform
                    };
                    _currentComicImage.MouseLeftButtonDown += Image_MouseLeftButtonDown;
                    _currentComicImage.MouseLeftButtonUp += Image_MouseLeftButtonUp;
                    _currentComicImage.MouseMove += Image_MouseMove;
                    _currentComicImage.MouseEnter += Image_MouseEnter;
                    _currentComicImage.MouseLeave += Image_MouseLeave;
                    _readerCenterGrid = new Grid
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    _readerCenterGrid.Children.Add(_currentComicImage);
                    _readerCenterGrid.MouseDown += (s, e2) =>
                    {
                        try { _readerScrollViewer?.Focus(); Keyboard.Focus(_readerScrollViewer); } catch { }
                    };

                    _readerScrollViewer = new ScrollViewer
                    {
                        Content = _readerCenterGrid,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        CanContentScroll = false,
                        PanningMode = PanningMode.Both,
                        Focusable = true
                    };
                    _readerScrollViewer.PreviewMouseWheel += (s, eargs) => { };
                    _readerScrollViewer.PreviewMouseDown += (s, eargs) =>
                    {
                        try { _readerScrollViewer?.Focus(); Keyboard.Focus(_readerScrollViewer); } catch { }
                    };
                }
                if (this.FindName("MainContentArea") is ContentControl content)
                    content.Content = _readerScrollViewer;
                CurrentView = _readerScrollViewer;
            }
        }

        public void EnterReadingMode()
        {
            // No abre archivo aún. Solo muestra el andamiaje del lector y la barra superior.
            try { Interlocked.Increment(ref _pageLoadSeq); } catch { }
            try { Interlocked.Increment(ref _thumbLoadSeq); } catch { }
            // Resetear estado de cómic abierto
            _isComicOpen = false;
            _currentPageIndex = 0;
            try { _comicLoader?.ClearCurrent(); } catch { }
            try { SettingsManager.Settings.LastOpenedFilePath = null; SettingsManager.Settings.LastOpenedPage = 0; SettingsManager.SaveSettings(); } catch { }
            EnsureReaderScaffold();
            // Mostrar un placeholder amigable en el centro si no hay imagen
            try
            {
                if (_currentComicImage != null)
                {
                    _currentComicImage.Source = null;
                }
                if (_readerCenterGrid != null)
                {
                    // Agregar un texto de ayuda si aún no existe
                    var existing = _readerCenterGrid.Children.OfType<TextBlock>().FirstOrDefault(tb => tb.Tag as string == "ReaderPlaceholder");
                    if (existing == null)
                    {
                        var help = new TextBlock
                        {
                            Tag = "ReaderPlaceholder",
                            Text = "No hay cómic abierto. Usa '📂 Abrir' para cargar uno.",
                            Foreground = System.Windows.Media.Brushes.Gray,
                            FontSize = 18,
                            Margin = new Thickness(12),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Center
                        };
                        _readerCenterGrid.Children.Add(help);
                    }
                }
            }
            catch { }
            SetReaderTopBarVisible(true);
            // Limpiar indicador/slider para que no muestre la última página del cómic anterior
            try { UpdatePageIndicator(); } catch { }
            OnPropertyChanged(nameof(IsComicViewActive));
            OnPropertyChanged(nameof(IsReaderViewActive));
        }

        private void EnterReadingMode_Click(object sender, RoutedEventArgs e) => EnterReadingMode();

        private void RemoveReaderPlaceholder()
        {
            try
            {
                if (_readerCenterGrid != null)
                {
                    var existing = _readerCenterGrid.Children.OfType<TextBlock>().FirstOrDefault(tb => tb.Tag as string == "ReaderPlaceholder");
                    if (existing != null) _readerCenterGrid.Children.Remove(existing);
                }
            }
            catch { }
        }

    private void LoadCurrentPage()
        {
            // En modo continuo, la materialización la gestiona ContinuousComicView
            if (SettingsManager.Settings?.EnableContinuousScroll == true) return;
            try
            {
                    if (_currentComicImage != null)
                    {
                        if (SettingsManager.Settings?.ShowLoadingIndicators == true)
                        {
                            if (this.FindName("PageIndicator") is TextBlock piLoad) piLoad.Text = "Cargando...";
                        }
                        // Id de petición para descartar resultados obsoletos
                        var requestId = Interlocked.Increment(ref _pageLoadSeq);
                        // Calcular ancho deseado: preferir el ancho del contenedor, fallback a viewport del ScrollViewer
                        int desiredWidth = 0;
                        try { desiredWidth = (int?)((this.FindName("MainContentArea") as FrameworkElement)?.ActualWidth) ?? (int)(_currentComicImage?.ActualWidth ?? 1200); } catch { desiredWidth = 1200; }
                        var prevScaling = System.Windows.Media.RenderOptions.GetBitmapScalingMode(_currentComicImage);
                        System.Windows.Media.RenderOptions.SetBitmapScalingMode(_currentComicImage, System.Windows.Media.BitmapScalingMode.LowQuality);
                        try
                        {
                            var sv = _readerScrollViewer as ScrollViewer;
                            if (sv != null)
                            {
                                desiredWidth = Math.Max(800, (int)sv.ViewportWidth);
                            }
                        }
                        catch { }

                        // Reacción inmediata: mostrar placeholder congelado
                        try
                        {
                            var ph = GetFrozen1x1Placeholder();
                            _currentComicImage.Source = ph;
                        }
                        catch { }

                        // Lanzar carga de miniatura en background y asignarla cuando est e9 lista (fire-and-forget)
                        try
                        {
                            var thumbToken = _ctsWindow.Token;
                            async Task HandleThumbAsync(long reqId, int pageIndex)
                            {
                                try
                                {
                                    var thumb = await _comicLoader.GetPageThumbnailAsync(pageIndex, 600, 0, thumbToken).ConfigureAwait(false);
                                    if (Volatile.Read(ref _pageLoadSeq) != reqId) return;
                                    var page = _comicLoader.Pages[pageIndex];
                                    page.Image = thumb;
                                    await this.Dispatcher.InvokeAsync(new Action(() =>
                                    {
                                        try { _currentComicImage.Source = thumb; } catch { }
                                    }), System.Windows.Threading.DispatcherPriority.Render).Task.ConfigureAwait(false);
                                }
                                catch { }
                            }
                            var _thumbHandler = HandleThumbAsync(requestId, _currentPageIndex);
                            TrackBackgroundTask(_thumbHandler);
                        }
                        catch { }

                        // Lanzar carga completa en background; cuando termine, aplicar transición y efectos en UI thread
                        var token = _ctsWindow.Token;
                        // Capturar estado al inicio de la carga: si el usuario cambia de modo
                        // mientras esta corriendo, la decision se mantiene consistente.
                        bool wantDouble = SettingsManager.Settings?.DoublePageEnabled == true
                                          && SettingsManager.Settings?.EnableContinuousScroll != true;
                        int pairIdx = _currentPageIndex + 1;
                        var _fullLoad = Task.Run(async () =>
                        {
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            BitmapImage bmp = null;
                            BitmapImage bmpRight = null;
                            try
                            {
                                if (token.IsCancellationRequested) return;
                                bmp = await _comicLoader.GetPageImageAsync(_currentPageIndex, desiredWidth).ConfigureAwait(false);
                                if (wantDouble && bmp != null && pairIdx < _comicLoader.Pages.Count)
                                {
                                    try { bmpRight = await _comicLoader.GetPageImageAsync(pairIdx, desiredWidth).ConfigureAwait(false); } catch { }
                                }
                            }
                            catch { }
                            sw.Stop();

                            // Si la petición fue invalidada por un cambio de página, o cancelada, abandonar
                            if (Volatile.Read(ref _pageLoadSeq) != requestId || token.IsCancellationRequested) return;

                            // Asegurar valor no nulo
                            if (bmp == null) bmp = GetFrozen1x1Placeholder();

                            // Ejecutar cambios en UI
                            try
                            {
                                await this.Dispatcher.InvokeAsync(new Action(() =>
                                {
                                    try
                                    {
                                        var page = _comicLoader.Pages[_currentPageIndex];
                                        // Aplicar brillo/contraste si procede
                                        var s = SettingsManager.Settings;
                                        // Imagen final que se mostrara: en doble pagina, es la composicion.
                                        System.Windows.Media.ImageSource finalSource = null;
                                        if (s != null && (Math.Abs(s.Brightness - 1.0) > 0.001 || Math.Abs(s.Contrast - 1.0) > 0.001))
                                        {
                                            try
                                            {
                                                var adjusted = ImageAdjuster.ApplyBrightnessContrast(bmp, s.Brightness, s.Contrast);
                                                page.Image = adjusted as BitmapImage ?? bmp;
                                                finalSource = adjusted;
                                            }
                                            catch { page.Image = bmp; finalSource = bmp; }
                                        }
                                        else
                                        {
                                            page.Image = bmp;
                                            finalSource = page.Image ?? bmp;
                                        }
                                        // Si esta activo doble pagina y se logro cargar la pareja, componer.
                                        if (wantDouble && bmpRight != null && finalSource is System.Windows.Media.Imaging.BitmapSource leftBs)
                                        {
                                            try
                                            {
                                                // Aplicar el mismo brillo/contraste a la pagina derecha para que ambas
                                                // se vean uniformes. ImageAdjuster puede devolver cualquier ImageSource.
                                                System.Windows.Media.Imaging.BitmapSource rightAdjusted = bmpRight;
                                                if (s != null && (Math.Abs(s.Brightness - 1.0) > 0.001 || Math.Abs(s.Contrast - 1.0) > 0.001))
                                                {
                                                    try
                                                    {
                                                        var adjR = ImageAdjuster.ApplyBrightnessContrast(bmpRight, s.Brightness, s.Contrast);
                                                        if (adjR is System.Windows.Media.Imaging.BitmapSource adjRBs) rightAdjusted = adjRBs;
                                                    }
                                                    catch { }
                                                }
                                                bool rtl = s?.CurrentReadingDirection == ReadingDirection.RightToLeft;
                                                var leftImg = rtl ? rightAdjusted : leftBs;
                                                var rightImg = rtl ? leftBs : rightAdjusted;
                                                var composed = ComposeDoublePage(leftImg, rightImg);
                                                if (composed != null) finalSource = composed;
                                            }
                                            catch { }
                                        }
                                        _currentComicImage.Source = finalSource;

                                        // Forzar escalado de alta calidad cuando la imagen completa está lista
                                        System.Windows.Media.RenderOptions.SetBitmapScalingMode(_currentComicImage, System.Windows.Media.BitmapScalingMode.HighQuality);

                                        // Transición suave: crear una superposición con la imagen anterior y desvanecerla
                                        Image overlay = null;
                                        try
                                        {
                                            if (_readerCenterGrid != null && _currentComicImage.Source != null)
                                            {
                                                overlay = new Image
                                                {
                                                    Source = _currentComicImage.Source,
                                                    HorizontalAlignment = HorizontalAlignment.Center,
                                                    VerticalAlignment = VerticalAlignment.Center,
                                                    Stretch = _currentComicImage.Stretch,
                                                    Opacity = 1.0
                                                };
                                                Panel.SetZIndex(overlay, 10);
                                                _readerCenterGrid.Children.Add(overlay);
                                            }
                                        }
                                        catch { }

                                        // Añadir un deslizamiento sutil según dirección de navegación
                                        try
                                        {
                                            int dir = _lastNavDirection;
                                            if (dir != 0)
                                            {
                                                var readingDirRtl = SettingsManager.Settings?.CurrentReadingDirection == ReadingDirection.RightToLeft;
                                                int visualDir = readingDirRtl ? -dir : dir;
                                                var tt = new System.Windows.Media.TranslateTransform();
                                                _currentComicImage.RenderTransform = new System.Windows.Media.TransformGroup
                                                {
                                                    Children = new System.Windows.Media.TransformCollection
                                                    {
                                                        new System.Windows.Media.ScaleTransform(_zoomFactor, _zoomFactor),
                                                        tt
                                                    }
                                                };
                                                _currentComicImage.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                                                double fromX = visualDir > 0 ? 24 : -24;
                                                tt.X = fromX;
                                                var slide = new System.Windows.Media.Animation.DoubleAnimation
                                                {
                                                    From = fromX,
                                                    To = 0,
                                                    Duration = new Duration(TimeSpan.FromMilliseconds(160)),
                                                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                                                };
                                                tt.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slide);
                                            }
                                        }
                                        catch { }

                                        // Iniciar fundido de la superposición (si existe)
                                        if (overlay != null)
                                        {
                                            try
                                            {
                                                var fade = new System.Windows.Media.Animation.DoubleAnimation
                                                {
                                                    From = 1.0,
                                                    To = 0.0,
                                                    Duration = new Duration(TimeSpan.FromMilliseconds(160)),
                                                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                                                };
                                                fade.Completed += (_, __) =>
                                                {
                                                    try { _readerCenterGrid.Children.Remove(overlay); } catch { }
                                                };
                                                overlay.BeginAnimation(UIElement.OpacityProperty, fade);
                                            }
                                            catch { try { _readerCenterGrid.Children.Remove(overlay); } catch { } }
                                        }

                                        UpdatePageIndicator();
                                        ApplyZoomToImage();
                                        ApplyReadingModeEffects();
                                        // Registrar pagina vista (1-based) en estadisticas.
                                        // En doble pagina, registrar tambien la derecha si fue cargada.
                                        _stats?.RecordPageViewed(_currentPageIndex + 1);
                                        try
                                        {
                                            bool dblStat = SettingsManager.Settings?.DoublePageEnabled == true
                                                           && SettingsManager.Settings?.EnableContinuousScroll != true;
                                            if (dblStat && bmpRight != null && _currentPageIndex + 1 < _comicLoader.Pages.Count)
                                                _stats?.RecordPageViewed(_currentPageIndex + 2);
                                        }
                                        catch { }
                                        // Guardar progreso
                                        SettingsManager.Settings.LastOpenedFilePath = _comicLoader.FilePath;
                                        SettingsManager.Settings.LastOpenedPage = _currentPageIndex;
                                        SettingsManager.SaveSettings();
                                    }
                                    catch { }
                                }), System.Windows.Threading.DispatcherPriority.Render);

                                // Precargar páginas adyacentes para navegación más fluida (fuera del dispatcher)
                                try { await PreloadAdjacentPages().ConfigureAwait(false); } catch { }
                            }
                            catch { }
                        }, token);
                        TrackBackgroundTask(_fullLoad);
                    }
                }
                catch (Exception ex)
                {
                    ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(
                        ex, 
                        "Carga de página",
                        ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Notify
                    );
                }
            }

        private async Task PreloadAdjacentPages()
        {
            if (SettingsManager.Settings?.EnableContinuousScroll == true) { await Task.CompletedTask; return; }
            try
            {
                // Usa la ventana de prefetch configurada (bidireccional) para preparar varias páginas alrededor
                _comicLoader.PreloadPages(_currentPageIndex);
            }
            catch { }
            await Task.CompletedTask;
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            ShowHomeView();
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Seleccionar Cómic",
                Filter = "Archivos de Cómic|*.cbz;*.cbr;*.pdf;*.epub|" +
                        "Archivos CBZ|*.cbz|" +
                        "Archivos CBR|*.cbr|" +
                        "Archivos PDF|*.pdf|" +
                        "Archivos EPUB|*.epub|" +
                        "Imágenes|*.jpg;*.jpeg;*.png;*.gif;*.bmp|" +
                        "Todos los archivos|*.*",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                OpenComicFile(openFileDialog.FileName);
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsView();
        }

        private void ToggleFavorites_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Use the shared instance from App so all windows/controls bind to the same VM
                var app = System.Windows.Application.Current as App;
                var sharedVm = app?.FavoritesViewModel;

                // Ensure the control receives the shared ViewModel
                var favSection = this.FindName("FavoritesSection") as System.Windows.Controls.UserControl;
                if (favSection != null && sharedVm != null) favSection.DataContext = sharedVm;

                var panel = this.FindName("FavoritesPanel") as FrameworkElement;
                var col = this.FindName("FavoritesCol") as System.Windows.Controls.ColumnDefinition;
                if (panel == null || col == null) return;
                if (panel.Visibility == Visibility.Collapsed)
                {
                    panel.Visibility = Visibility.Visible;
                    col.Width = new GridLength(320);
                }
                else
                {
                    panel.Visibility = Visibility.Collapsed;
                    col.Width = new GridLength(0);
                }
            }
            catch { }
        }

        private async void ToggleContinuous_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool enable = !(SettingsManager.Settings?.EnableContinuousScroll == true);
                SettingsManager.Settings.EnableContinuousScroll = enable;
                SettingsManager.SaveSettings();

                // Mostrar notificación animada breve
                ShowModeToast(enable ? "📖 Modo continuo activado" : "📄 Modo paginado activado");

                // Mantener progreso: si estamos en paginado, la página actual es _currentPageIndex;
                // en continuo, la vista maneja índices 0-based también.
                if (enable)
                {
                    // Cambiar a la vista continua
                    await _continuousView.LoadComicAsync(_comicLoader);
                    if (this.FindName("MainContentArea") is ContentControl content)
                    {
                        content.Content = _continuousView;
                        // ✅ Forzar layout update para evitar que el panel se despegue
                        content.UpdateLayout();
                    }
                    CurrentView = _continuousView;
                    // Asegurar sincronización: desplazar a la página actual
                    await System.Threading.Tasks.Task.Delay(100); // Aumentado a 100ms
                    try
                    {
                        _continuousView.ScrollToPage(_currentPageIndex);
                    }
                    catch { }
                    // Actualizar botón y navegación
                    if (this.FindName("ToggleContinuousButton") is Button tb) tb.Content = "📜";
                    UpdateNavButtonsForContinuousMode(true);
                    try { if (this.FindName("PrevButton") is System.Windows.Controls.Button pb) pb.IsEnabled = false; } catch { }
                    try { if (this.FindName("NextButton") is System.Windows.Controls.Button nb) nb.IsEnabled = false; } catch { }
                }
                else
                {
                    // Cambiar a paginado
                    try
                    {
                        // TODO: Implementar obtención de página actual con EnhancedContinuousComicView
                        // Si la vista continua existe, obtener el índice visible más reciente
                        // if (_continuousView?.ViewModel != null)
                        // {
                        //     // Asegurar que el ViewModel no está en modo programático
                        //     _continuousView.ViewModel.EndProgrammaticScroll();
                        //     var idx = _continuousView.ViewModel.CurrentPage;
                        //     if (idx >= 0 && idx < (_comicLoader?.PageCount ?? int.MaxValue))
                        //         _currentPageIndex = idx;
                        // }
                    }
                    catch { }
                    EnsureReaderScaffold();
                    if (this.FindName("MainContentArea") is ContentControl content)
                    {
                        content.Content = _readerScrollViewer;
                        // ✅ Forzar layout update
                        content.UpdateLayout();
                    }
                    // Restaurar página actual en la imagen
                    LoadCurrentPage();
                    if (this.FindName("ToggleContinuousButton") is Button tb) tb.Content = "📄";
                    UpdateNavButtonsForContinuousMode(false);
                    try { if (this.FindName("PrevButton") is System.Windows.Controls.Button pb) pb.IsEnabled = true; } catch { }
                    try { if (this.FindName("NextButton") is System.Windows.Controls.Button nb) nb.IsEnabled = true; } catch { }
                }
            }
            catch { }
        }

        // ============================================================
        // Modos de lectura (popup unificado en la barra del lector)
        // ============================================================
        private void ReaderModes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button b && b.ContextMenu != null)
                {
                    SyncReaderModesMenu();
                    b.ContextMenu.PlacementTarget = b;
                    b.ContextMenu.IsOpen = true;
                }
            }
            catch { }
        }

        private void SyncReaderModesMenu()
        {
            try
            {
                var s = SettingsManager.Settings;
                bool isContinuous = s?.EnableContinuousScroll == true;
                bool isVerticalPaged = !isContinuous && s?.VerticalPagedMode == true;
                bool isHorizontalPaged = !isContinuous && !isVerticalPaged;
                bool isRtl = s?.CurrentReadingDirection == ComicReader.Services.ReadingDirection.RightToLeft;
                bool doublePage = s?.DoublePageEnabled == true;
                string fit = (s?.DefaultFitMode ?? "width").ToLowerInvariant();

                if (this.FindName("MenuModeContinuous") is MenuItem mc) mc.IsChecked = isContinuous;
                if (this.FindName("MenuModePaged") is MenuItem mp) mp.IsChecked = isHorizontalPaged;
                if (this.FindName("MenuModeVerticalPaged") is MenuItem mv) mv.IsChecked = isVerticalPaged;

                // Doble pagina solo aplica en modo paginado (cualquier orientacion).
                if (this.FindName("MenuDoublePage") is MenuItem mdp)
                {
                    mdp.IsChecked = doublePage && !isContinuous;
                    mdp.IsEnabled = !isContinuous;
                }

                if (this.FindName("MenuFitWidth") is MenuItem mfw) mfw.IsChecked = fit == "width";
                if (this.FindName("MenuFitHeight") is MenuItem mfh) mfh.IsChecked = fit == "height";
                if (this.FindName("MenuFitScreen") is MenuItem mfs) mfs.IsChecked = fit == "screen";
                if (this.FindName("MenuFitOriginal") is MenuItem mfo) mfo.IsChecked = fit == "original";

                if (this.FindName("MenuDirLTR") is MenuItem ml) ml.IsChecked = !isRtl;
                if (this.FindName("MenuDirRTL") is MenuItem mr) mr.IsChecked = isRtl;
                if (this.FindName("MenuNightMode") is MenuItem mn)
                    mn.IsChecked = s?.IsNightMode == true;
                if (this.FindName("MenuThumbnails") is MenuItem mt && this.FindName("ThumbCol") is System.Windows.Controls.ColumnDefinition tc)
                    mt.IsChecked = tc.Width.Value > 0;
            }
            catch { }
        }

        private void MenuModeContinuous_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var s = SettingsManager.Settings;
                bool currentlyContinuous = s?.EnableContinuousScroll == true;
                if (!currentlyContinuous) ToggleContinuous_Click(null, null);
                if (s != null) { s.VerticalPagedMode = false; SettingsManager.SaveSettings(); }
                SyncReaderModesMenu();
            }
            catch { }
        }

        private void MenuModePaged_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var s = SettingsManager.Settings;
                bool currentlyContinuous = s?.EnableContinuousScroll == true;
                if (currentlyContinuous) ToggleContinuous_Click(null, null);
                if (s != null) { s.VerticalPagedMode = false; SettingsManager.SaveSettings(); }
                SyncReaderModesMenu();
            }
            catch { }
        }

        private void MenuModeVerticalPaged_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var s = SettingsManager.Settings;
                if (s == null) return;
                // Paginado vertical: forzar modo paginado y activar la flag.
                if (s.EnableContinuousScroll) ToggleContinuous_Click(null, null);
                s.VerticalPagedMode = true;
                SettingsManager.SaveSettings();
                ShowModeToast("Paginado vertical: flechas Arriba/Abajo cambian de pagina");
                SyncReaderModesMenu();
            }
            catch { }
        }

        private void MenuDoublePage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var s = SettingsManager.Settings;
                if (s == null) return;
                // Solo aplica en paginado.
                if (s.EnableContinuousScroll) { ShowModeToast("Doble pagina no aplica en lectura continua"); SyncReaderModesMenu(); return; }
                s.DoublePageEnabled = !s.DoublePageEnabled;
                // Snap el indice a la primera pagina del par cuando se activa doble.
                try
                {
                    if (s.DoublePageEnabled && (_currentPageIndex & 1) == 1)
                    {
                        _currentPageIndex = Math.Max(0, _currentPageIndex - 1);
                    }
                }
                catch { }
                SettingsManager.SaveSettings();
                ShowModeToast(s.DoublePageEnabled ? "Doble pagina: ON" : "Doble pagina: OFF");
                // Recargar pagina actual para que se componga el par.
                try { Interlocked.Increment(ref _pageLoadSeq); LoadCurrentPage(); UpdatePageIndicator(); } catch { }
                SyncReaderModesMenu();
            }
            catch { }
        }

        private void ApplyFitMode(string mode)
        {
            try
            {
                var s = SettingsManager.Settings;
                if (s == null) return;
                s.DefaultFitMode = mode;
                SettingsManager.SaveSettings();
                switch (mode)
                {
                    case "height": ApplyFitToHeight(); break;
                    case "screen": FitToScreen(); break;
                    case "original": _zoomFactor = 1.0; ApplyZoomToImage(); UpdateZoomIndicator(); break;
                    case "width":
                    default: FitToWidth(); break;
                }
                SyncReaderModesMenu();
            }
            catch { }
        }

        private void MenuFitWidth_Click(object sender, RoutedEventArgs e) => ApplyFitMode("width");
        private void MenuFitHeight_Click(object sender, RoutedEventArgs e) => ApplyFitMode("height");
        private void MenuFitScreen_Click(object sender, RoutedEventArgs e) => ApplyFitMode("screen");
        private void MenuFitOriginal_Click(object sender, RoutedEventArgs e) => ApplyFitMode("original");

        private void MenuDirLTR_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SettingsManager.Settings != null)
                {
                    SettingsManager.Settings.CurrentReadingDirection = ComicReader.Services.ReadingDirection.LeftToRight;
                    SettingsManager.SaveSettings();
                    ShowModeToast("Direccion: izquierda a derecha");
                }
                SyncReaderModesMenu();
            }
            catch { }
        }

        private void MenuDirRTL_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SettingsManager.Settings != null)
                {
                    SettingsManager.Settings.CurrentReadingDirection = ComicReader.Services.ReadingDirection.RightToLeft;
                    SettingsManager.SaveSettings();
                    ShowModeToast("Direccion: derecha a izquierda (manga)");
                }
                SyncReaderModesMenu();
            }
            catch { }
        }

        private void ShowModeToast(string text)
        {
            try
            {
                // Crear un borde temporal en la ventana para mostrar el mensaje
                var toast = new System.Windows.Controls.Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 30, 30, 30)),
                    CornerRadius = new System.Windows.CornerRadius(8),
                    Padding = new System.Windows.Thickness(12),
                    Opacity = 0,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new System.Windows.Thickness(0, 56, 0, 0)
                };
                var tb = new System.Windows.Controls.TextBlock
                {
                    Text = text,
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold
                };
                toast.Child = tb;
                var root = this.Content as FrameworkElement;
                if (root == null) return;
                if (root is Panel panel)
                {
                    panel.Children.Add(toast);
                }
                else if (this.FindName("CustomTitleBar") is FrameworkElement)
                {
                    // Fallback: añadir al grid principal
                    var grid = this.Content as Grid;
                    grid?.Children.Add(toast);
                }

                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220));
                var stay = new System.Windows.Media.Animation.DoubleAnimation(1, 1, TimeSpan.FromMilliseconds(1100)) { BeginTime = TimeSpan.FromMilliseconds(220) };
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(1320) };
                var sb = new System.Windows.Media.Animation.Storyboard();
                sb.Children.Add(fadeIn); sb.Children.Add(stay); sb.Children.Add(fadeOut);
                System.Windows.Media.Animation.Storyboard.SetTarget(fadeIn, toast); System.Windows.Media.Animation.Storyboard.SetTargetProperty(fadeIn, new PropertyPath(Border.OpacityProperty));
                System.Windows.Media.Animation.Storyboard.SetTarget(stay, toast); System.Windows.Media.Animation.Storyboard.SetTargetProperty(stay, new PropertyPath(Border.OpacityProperty));
                System.Windows.Media.Animation.Storyboard.SetTarget(fadeOut, toast); System.Windows.Media.Animation.Storyboard.SetTargetProperty(fadeOut, new PropertyPath(Border.OpacityProperty));
                sb.Completed += (_, __) => { try { if (root is Panel p) p.Children.Remove(toast); else (this.Content as Grid)?.Children.Remove(toast); } catch { } };
                sb.Begin();
            }
            catch { }
        }

    private async void OpenComicFile(string filePath)
        {
            try
            {
                // ✅ VALIDACIÓN: Verificar que el archivo es válido ANTES de intentar cargarlo
                var validationResult = ComicReader.Services.Validation.ValidationService.Instance.ValidateComicFile(filePath);
                if (!validationResult.IsValid)
                {
                    ComicReader.Services.Notifications.NotificationService.Instance.Error(
                        validationResult.ErrorMessage,
                        "Archivo inválido"
                    );
                    return;
                }

                // Invalida cualquier carga de páginas/miniaturas anterior
                Interlocked.Increment(ref _pageLoadSeq);
                Interlocked.Increment(ref _thumbLoadSeq);
                RemoveReaderPlaceholder();
                if (this.FindName("PageIndicator") is TextBlock pi) pi.Text = "Cargando...";
                
                // ✅ LOADING: Mostrar feedback mientras carga
                await _comicLoader.LoadComicAsync(filePath);
                _comicLoader.RefreshTuningFromSettings();
                
                if (_comicLoader.Pages.Count > 0)
                {
                    // Reset de UI del lector por si venimos del menú
                    if (_currentComicImage == null)
                    {
                        _currentComicImage = new Image
                        {
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Stretch = System.Windows.Media.Stretch.Uniform
                        };
                        // Asegurar manejadores para pan/zoom
                        _currentComicImage.MouseLeftButtonDown += Image_MouseLeftButtonDown;
                        _currentComicImage.MouseLeftButtonUp += Image_MouseLeftButtonUp;
                        _currentComicImage.MouseMove += Image_MouseMove;
                        _currentComicImage.MouseEnter += Image_MouseEnter;
                        _currentComicImage.MouseLeave += Image_MouseLeave;
                    }
                    // Si el panel de miniaturas sigue abierto desde un cómic anterior, refrescar su contenido ahora
                    if (_thumbnailsVisible)
                    {
                        RefreshThumbnailPanelForCurrentComic();
                    }
                    if (_readerCenterGrid == null)
                    {
                        _readerCenterGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    }
                    _readerCenterGrid.Children.Clear();
                    _readerCenterGrid.Children.Add(_currentComicImage);
                    if (_readerScrollViewer == null)
                    {
                        _readerScrollViewer = new ScrollViewer
                        {
                            Content = _readerCenterGrid,
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            CanContentScroll = false
                        };
                    }
                    else
                    {
                        _readerScrollViewer.Content = _readerCenterGrid;
                    }
                        // Determinar página inicial por progreso previo o si está en completados
                        int startIndex = 0;
                        try
                        {
                            var svc = ComicReader.Services.ContinueReadingService.Instance;
                            var existing = svc.Items.FirstOrDefault(x => string.Equals(x.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
                            var completed = svc.CompletedItems.FirstOrDefault(x => string.Equals(x.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

                            // Si está en completados y no hay un item en Items, mostrar diálogo creativo
                            if (completed != null && existing == null)
                            {
                                try
                                {
                                    var dlg = new ReopenCompletedDialog();
                                    // preparar portada y metadatos
                                    BitmapImage cover = null;
                                    try { cover = completed.CoverThumbnail; } catch { }
                                    dlg.SetInfo(completed.DisplayName ?? Path.GetFileNameWithoutExtension(filePath), completed.DateCompleted?.ToString("dd/MM/yyyy"), cover);
                                    dlg.Owner = this;
                                    if (dlg.ShowDialog() == true)
                                    {
                                        var choice = dlg.Choice;
                                        if (choice == ReopenCompletedDialog.OpenChoice.Start)
                                        {
                                            startIndex = 0;
                                        }
                                        else if (choice == ReopenCompletedDialog.OpenChoice.Continue)
                                        {
                                            startIndex = Math.Max(0, Math.Min(_comicLoader.Pages.Count - 1, (completed.LastPage > 0 ? completed.LastPage - 1 : 0)));
                                        }
                                        else // Unmark
                                        {
                                            try { svc.Remove(filePath); } catch { }
                                            startIndex = 0;
                                            // Refresh home view lists
                                            try { _homeView?.RefreshRecent(); } catch { }
                                        }
                                    }
                                    else
                                    {
                                        // Usuario canceló: abrir igual pero mantener posición de inicio 0
                                        startIndex = 0;
                                    }
                                }
                                catch { startIndex = 0; }
                            }
                            else if (existing != null)
                            {
                                int last1 = Math.Max(1, existing.LastPage);
                                startIndex = Math.Min(_comicLoader.Pages.Count - 1, last1 - 1);
                            }
                            else if (completed != null)
                            {
                                // fallback: if completed but an item existed earlier, continue from end
                                startIndex = 0;
                            }
                        }
                        catch { }
                    _currentPageIndex = Math.Max(0, Math.Min(_comicLoader.Pages.Count - 1, startIndex));
                    // Iniciar sesión de lectura
                    _stats?.StartSession(filePath, _comicLoader.ComicTitle, _comicLoader.Pages.Count);
                    
                    // Registrar en "Seguir leyendo" con la página correcta
                    ComicReader.Services.ContinueReadingService.Instance.UpsertProgress(filePath, _currentPageIndex + 1, _comicLoader.Pages.Count);
                    try { _homeView?.RefreshRecent(); } catch { }

                    ShowComicView();
                    // Normalizar UI de paginación al abrir correctamente
                    try { UpdatePageIndicator(); } catch { }
                    // Entrar automáticamente en modo inmersivo si está activado en ajustes
                    try
                    {
                        if (SettingsManager.Settings?.AutoEnterImmersiveOnOpen == true)
                        {
                            ToggleImmersiveFullScreen();
                        }
                    }
                    catch { }
                    // En modo continuo, desplazarse a la página guardada
                    if (SettingsManager.Settings?.EnableContinuousScroll == true && _continuousView != null)
                    {
                        _continuousView.ScrollToPage(_currentPageIndex);
                    }
                    // Si el usuario activó precarga completa, lanzar la precarga con ventana de progreso
                    try
                    {
                        if (SettingsManager.Settings?.EnableEagerPreload == true)
                        {
                            var loaderSvc = ComicReader.Core.Services.ServiceLocator.TryGet<ComicReader.Core.Abstractions.IComicPageLoader>();
                            if (loaderSvc is ComicReader.Services.ProgressivePageLoader ploader)
                            {
                                // Ejecutar la precarga en background de forma silenciosa (sin ventana de progreso)
                                var progress = new Progress<(int done, int total)>(t =>
                                {
                                    // Opcional: podríamos escribir en logs o actualizar un HUD no modal.
                                    try { Logger.Log($"Preload progress: {t.done}/{t.total}"); } catch { }
                                });
                                var cts = CancellationTokenSource.CreateLinkedTokenSource(_ctsWindow.Token);
                                var preloadTask = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await ploader.EagerPreloadAllAsync(_currentPageIndex, SettingsManager.Settings?.EagerPreloadConcurrency ?? 3, cts.Token, progress).ConfigureAwait(false);
                                    }
                                    catch (OperationCanceledException) { }
                                    catch (Exception ex) { try { Logger.LogException("EagerPreloadAllAsync failed", ex); } catch { } }
                                }, cts.Token);
                                TrackBackgroundTask(preloadTask);
                            }
                        }
                    }
                    catch { }
                    EnsureAutoAdvanceBehavior();
                    
                    // Registrar archivo reciente en v3.0
                    try
                    {
                        await ComicReader.Services.PersistenceIntegrator.Instance.AddRecentFileAsync(filePath);
                        ComicReader.Utils.ModernLogger.Info($"✓ Archivo reciente registrado: {Path.GetFileName(filePath)}");
                    }
                    catch (Exception ex)
                    {
                        ComicReader.Utils.ModernLogger.Error($"Error registrando archivo reciente: {ex.Message}");
                    }
                }
                else
                {
                    ComicReader.Services.Notifications.NotificationService.Instance.Error(
                        "No se pudieron cargar las páginas del cómic",
                        "Error de carga"
                    );
                }
            }
            catch (Exception ex)
            {
                ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(
                    ex, 
                    "Apertura de archivo",
                    ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Notify
                );
            }
        }

        public void PrevPage_Click(object sender, RoutedEventArgs e)
        {
            try { ComicReader.Utils.DevLogger.Debug($"PrevPage_Click invoked. EnableContinuous={SettingsManager.Settings?.EnableContinuousScroll}"); } catch { }
            // En modo continuo, el botón Prev debe desplazar hacia arriba en el viewport
            if (SettingsManager.Settings?.EnableContinuousScroll == true && _continuousView != null)
            {
                try { var moved = ScrollContinuousWithinView(down: false); ComicReader.Utils.DevLogger.Debug($"PrevPage_Click -> ScrollContinuousWithinView returned {moved}"); if (moved) UpdatePageIndicator(); } catch (Exception ex) { ComicReader.Utils.DevLogger.Debug($"PrevPage_Click exception: {ex}"); }
                return;
            }
            // Modo paginado (comportamiento original). Avanza por 2 si esta activo doble pagina.
            if (_currentPageIndex > 0)
            {
                bool dbl = SettingsManager.Settings?.DoublePageEnabled == true;
                int step = dbl ? 2 : 1;
                int target = Math.Max(0, _currentPageIndex - step);
                if (dbl) target = target & ~1; // snap a inicio de par
                _currentPageIndex = target;
                _lastNavDirection = -1;
                // Cancelar carga anterior y cargar nueva
                Interlocked.Increment(ref _pageLoadSeq);
                LoadCurrentPage();
                UpdatePageIndicator();
                // Prefetch adicional direccional (dos páginas más atrás)
                        try
                    {
                        int p2 = _currentPageIndex - 1; // ya se precarga -1 en LoadCurrentPage
                        int p3 = _currentPageIndex - 2;
                        if (p3 >= 0) TrackBackgroundTask(_comicLoader.GetPageImageAsync(p3, 1200));
                        int p4 = _currentPageIndex - 3;
                        if (p4 >= 0) TrackBackgroundTask(_comicLoader.GetPageImageAsync(p4, 1200));
                    }
                catch { }
                // Si el panel de miniaturas está visible, solo sincronizar selección
                if (_thumbnailsVisible)
                {
                    try
                    {
                        var list = this.FindName("ThumbList") as System.Windows.Controls.ListBox;
                        if (list != null)
                        {
                            _suppressThumbListSelectionChange = true;
                            try
                            {
                                list.ItemsSource = _comicLoader.Pages;
                                list.SelectedIndex = _currentPageIndex;
                                list.ScrollIntoView(list.SelectedItem);
                            }
                            finally { _suppressThumbListSelectionChange = false; }
                        }
                    }
                    catch { }
                }
                // Actualizar progreso en servicio. En doble pagina,
                // EffectiveProgressOneBased reporta la segunda del par cuando hay dos visibles.
                try { if (_isComicOpen) ComicReader.Services.ContinueReadingService.Instance.UpsertProgress(_comicLoader.FilePath, EffectiveProgressOneBased(), _comicLoader.PageCount); } catch { }
            }
        }

        public void NextPage_Click(object sender, RoutedEventArgs e)
        {
            try { ComicReader.Utils.DevLogger.Debug($"NextPage_Click invoked. EnableContinuous={SettingsManager.Settings?.EnableContinuousScroll}"); } catch { }
            // En modo continuo, el botón Next debe desplazar hacia abajo en el viewport
            if (SettingsManager.Settings?.EnableContinuousScroll == true && _continuousView != null)
            {
                try { var moved = ScrollContinuousWithinView(down: true); ComicReader.Utils.DevLogger.Debug($"NextPage_Click -> ScrollContinuousWithinView returned {moved}"); if (moved) UpdatePageIndicator(); } catch (Exception ex) { ComicReader.Utils.DevLogger.Debug($"NextPage_Click exception: {ex}"); }
                return;
            }
            // Modo paginado (comportamiento original). Avanza por 2 si esta activo doble pagina.
            if (_currentPageIndex < _comicLoader.Pages.Count - 1)
            {
                bool dbl = SettingsManager.Settings?.DoublePageEnabled == true;
                int step = dbl ? 2 : 1;
                int target = Math.Min(_comicLoader.Pages.Count - 1, _currentPageIndex + step);
                if (dbl) target = target & ~1; // snap a inicio de par
                if (target == _currentPageIndex) return; // ya estamos en el ultimo par; evita recarga + flicker
                _currentPageIndex = target;
                _lastNavDirection = 1;
                // Cancelar carga anterior y cargar nueva
                Interlocked.Increment(ref _pageLoadSeq);
                LoadCurrentPage();
                UpdatePageIndicator();
                // Prefetch adicional direccional
                try
                {
                    int n2 = _currentPageIndex + 1;
                    int n3 = _currentPageIndex + 2;
                    if (n3 < _comicLoader.Pages.Count) TrackBackgroundTask(_comicLoader.GetPageImageAsync(n3, 1200));
                    int n4 = _currentPageIndex + 3;
                    if (n4 < _comicLoader.Pages.Count) TrackBackgroundTask(_comicLoader.GetPageImageAsync(n4, 1200));
                }
                catch { }
                // Actualizar progreso. En doble pagina, si hay pagina derecha
                // visible (_currentPageIndex + 1 dentro del rango), el progreso
                // efectivo es la segunda pagina del par; sin esto, comics con
                // total par nunca dispararian la regla de finalizacion (oneBased
                // < PageCount al llegar al ultimo par).
                try
                {
                    if (_isComicOpen)
                    {
                        var oneBased = EffectiveProgressOneBased();
                        ComicReader.Services.ContinueReadingService.Instance.UpsertProgress(_comicLoader.FilePath, oneBased, _comicLoader.PageCount);
                        if (oneBased >= _comicLoader.PageCount)
                        {
                            _homeView?.RefreshRecent();
                        }
                    }
                }
                catch { }
            }
        }

        // En doble pagina, cuando hay par visible, el progreso reportado debe
        // contar las dos paginas. En cualquier otro caso es _currentPageIndex + 1.
        private int EffectiveProgressOneBased()
        {
            try
            {
                bool dbl = SettingsManager.Settings?.DoublePageEnabled == true
                           && SettingsManager.Settings?.EnableContinuousScroll != true;
                int total = _comicLoader?.Pages?.Count ?? 0;
                if (dbl && total > 0 && _currentPageIndex + 1 < total)
                    return _currentPageIndex + 2;
                return _currentPageIndex + 1;
            }
            catch { return _currentPageIndex + 1; }
        }

        public void GoToPage_Click(object sender, RoutedEventArgs e)
        {
            if (!_isComicOpen || _comicLoader.Pages.Count == 0) return;

            var dialog = new GoToPageDialog(_comicLoader.Pages.Count, _currentPageIndex + 1);
            if (dialog.ShowDialog() == true)
            {
                int targetPage = dialog.SelectedPage - 1; // Convert to 0-based index
                if (targetPage >= 0 && targetPage < _comicLoader.Pages.Count)
                {
                    _lastNavDirection = targetPage > _currentPageIndex ? 1 : (targetPage < _currentPageIndex ? -1 : 0);
                    _currentPageIndex = targetPage;
                    if (SettingsManager.Settings?.EnableContinuousScroll == true && _continuousView != null)
                    {
                        // Desplazar en modo continuo
                        _continuousView.ScrollToPage(targetPage);
                    }
                    else
                    {
                        // Modo clásico
                        LoadCurrentPage();
                    }
                    UpdatePageIndicator();
                    // Actualizar progreso y manejar finalizacion
                    try
                    {
                        var oneBased = EffectiveProgressOneBased();
                        ComicReader.Services.ContinueReadingService.Instance.UpsertProgress(_comicLoader.FilePath, oneBased, _comicLoader.PageCount);
                        if (oneBased >= _comicLoader.PageCount)
                        {
                            // See note: don't call Remove() — UpsertProgress handles moving to completed.
                            _homeView?.RefreshRecent();
                        }
                    }
                    catch { }
                }
            }
        }

        private void UpdatePageIndicator()
        {
            bool noComic = !_isComicOpen || _comicLoader == null || _comicLoader.Pages == null || _comicLoader.Pages.Count == 0;
            if (this.FindName("PageIndicator") is TextBlock pi2)
            {
                if (SettingsManager.Settings?.ShowPageNumberOverlay == true)
                {
                    pi2.Visibility = Visibility.Visible;
                    if (noComic)
                    {
                        pi2.Text = "— / —";
                    }
                    else
                    {
                        bool dbl = SettingsManager.Settings?.DoublePageEnabled == true
                                   && SettingsManager.Settings?.EnableContinuousScroll != true;
                        int total = _comicLoader.Pages.Count;
                        if (dbl && _currentPageIndex + 1 < total)
                            pi2.Text = $"Páginas {_currentPageIndex + 1}-{_currentPageIndex + 2} de {total}";
                        else
                            pi2.Text = $"Página {_currentPageIndex + 1} de {total}";
                    }
                }
                else
                {
                    pi2.Visibility = Visibility.Collapsed;
                }
            }
            if (this.FindName("PageSlider") is Slider slider)
            {
                if (noComic)
                {
                    slider.Minimum = 1;
                    slider.Maximum = 1;
                    slider.Value = 1;
                    slider.IsEnabled = false;
                }
                else
                {
                    slider.Minimum = 1;
                    slider.Maximum = Math.Max(1, _comicLoader.Pages.Count);
                    slider.Value = _currentPageIndex + 1;
                    slider.IsEnabled = true;
                }
            }
            // Actualizar barra de progreso visual
            if (this.FindName("ReaderProgressBar") is ProgressBar rpb)
            {
                if (noComic)
                {
                    rpb.Value = 0;
                    rpb.Visibility = Visibility.Collapsed;
                }
                else
                {
                    int pageCount = Math.Max(1, _comicLoader.Pages.Count);
                    double percent = ((_currentPageIndex + 1) * 100.0) / pageCount;
                    rpb.Value = Math.Max(0, Math.Min(100, percent));
                    rpb.Visibility = Visibility.Visible;
                }
            }
            // Mantener sincronizada la selección del panel de miniaturas si está visible
            if (_thumbnailsVisible)
            {
                try
                {
                    var list = this.FindName("ThumbList") as System.Windows.Controls.ListBox;
                    if (list != null && ReferenceEquals(list.ItemsSource, _comicLoader.Pages))
                    {
                        _suppressThumbListSelectionChange = true;
                        try
                        {
                            list.SelectedIndex = Math.Max(0, Math.Min(_currentPageIndex, _comicLoader.Pages.Count - 1));
                            list.ScrollIntoView(list.SelectedItem);
                        }
                        finally { _suppressThumbListSelectionChange = false; }
                    }
                }
                catch { }
            }
        }

        private bool _isSliderChanging = false;
        private void PageSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isSliderChanging) return;
            if (!_isComicOpen || _comicLoader.Pages.Count == 0) return;

            int newPage = (int)Math.Round(e.NewValue) - 1;
            newPage = Math.Max(0, Math.Min(_comicLoader.Pages.Count - 1, newPage));
            if (newPage != _currentPageIndex)
            {
                _currentPageIndex = newPage;
                if (SettingsManager.Settings?.EnableContinuousScroll == true && _continuousView != null)
                {
                    _continuousView.ScrollToPage(newPage);
                }
                else
                {
                    LoadCurrentPage();
                }
                _isSliderChanging = true;
                try { UpdatePageIndicator(); } finally { _isSliderChanging = false; }
                // Actualizar progreso en servicio y manejar finalizacion
                try
                {
                    if (_isComicOpen)
                    {
                        var oneBased = EffectiveProgressOneBased();
                        ComicReader.Services.ContinueReadingService.Instance.UpsertProgress(_comicLoader.FilePath, oneBased, _comicLoader.PageCount);
                        if (oneBased >= _comicLoader.PageCount)
                        {
                            // Avoid removing the item entirely. Refresh home view to reflect Completed state.
                            _homeView?.RefreshRecent();
                        }
                    }
                }
                catch { }
            }
        }

        public void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            AdjustZoom(1.25); // Incremento de 25%
        }

        public void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            AdjustZoom(0.8); // Decremento de 20%
        }

        public void ZoomReset_Click(object sender, RoutedEventArgs e)
        {
            SetZoom(1.0);
        }

        public void Zoom25_Click(object sender, RoutedEventArgs e) => SetZoom(0.25);
        public void Zoom50_Click(object sender, RoutedEventArgs e) => SetZoom(0.5);
        public void Zoom75_Click(object sender, RoutedEventArgs e) => SetZoom(0.75);
        public void Zoom100_Click(object sender, RoutedEventArgs e) => SetZoom(1.0);
        public void Zoom125_Click(object sender, RoutedEventArgs e) => SetZoom(1.25);
        public void Zoom150_Click(object sender, RoutedEventArgs e) => SetZoom(1.5);
        public void Zoom200_Click(object sender, RoutedEventArgs e) => SetZoom(2.0);
        public void Zoom400_Click(object sender, RoutedEventArgs e) => SetZoom(4.0);

        private void AdjustZoom(double factor)
        {
            double newZoom = _zoomFactor * factor;
            SetZoom(newZoom);
        }

        private void SetZoom(double newZoom)
        {
            // Limitar zoom entre 10% y 800%
            _zoomFactor = Math.Max(0.1, Math.Min(8.0, newZoom));
            ApplyZoomToImage();
            UpdateZoomIndicator();
            _lastZoomChange = DateTime.UtcNow;
        }

    private double _lastZoomFactor = 1.0;
    private DateTime _lastZoomChange = DateTime.MinValue;
    private static readonly TimeSpan _interactionGrace = TimeSpan.FromSeconds(2);

        private void ApplyZoomToImage()
        {
            if (_currentComicImage != null)
            {
                // Capturar centro relativo antes del cambio de zoom
                ScrollViewer sv = null;
                if (_currentComicImage.Parent is Grid g && g.Parent is ScrollViewer sv1) sv = sv1;
                double centerRel = 0.0;
                double prevExtent = 0.0;
                if (sv != null)
                {
                    prevExtent = sv.ExtentHeight;
                    var oldCenter = sv.VerticalOffset + sv.ViewportHeight / 2.0;
                    centerRel = prevExtent > 0 ? (oldCenter / prevExtent) : 0.0;
                }

                var transformGroup = new System.Windows.Media.TransformGroup();
                
                // Aplicar zoom
                transformGroup.Children.Add(new System.Windows.Media.ScaleTransform(_zoomFactor, _zoomFactor));
                
                // Aplicar rotación si existe
                if (_rotationAngle != 0)
                {
                    transformGroup.Children.Add(new System.Windows.Media.RotateTransform(_rotationAngle));
                }
                
                _currentComicImage.RenderTransform = transformGroup;
                _currentComicImage.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                
                // Reposicionar manteniendo el mismo centro relativo tras el nuevo layout
                if (sv != null)
                {
                    this.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            var newExtent = sv.ExtentHeight;
                            var desiredCenter = newExtent * centerRel;
                            var targetOffset = Math.Max(0, desiredCenter - sv.ViewportHeight / 2.0);
                            // Limitar para no exceder el contenido
                            var maxOffset = Math.Max(0, newExtent - sv.ViewportHeight);
                            sv.ScrollToVerticalOffset(Math.Min(targetOffset, maxOffset));
                        }
                        catch { }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }

                _lastZoomFactor = _zoomFactor;
            }
        }

        private void UpdateZoomIndicator()
        {
            // El indicador de zoom se actualizará cuando se implemente en el XAML
            // Por ahora guardamos el valor para futuro uso
            System.Diagnostics.Debug.WriteLine($"Zoom: {(_zoomFactor * 100):F0}%");
        }

        // Mejora: zoom con Ctrl + rueda del ratón
        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (e.Delta > 0) AdjustZoom(1.1);
                else AdjustZoom(0.9);
                e.Handled = true;
            }
            else
            {
                // Navegación con rueda del mouse si no hay Ctrl; opcionalmente invertida
                if (_isComicOpen && _comicLoader.Pages.Count > 0 && SettingsManager.Settings?.EnableContinuousScroll != true)
                {
                    int delta = e.Delta;
                    bool invert = SettingsManager.Settings?.InvertScrollWheel == true;
                    if ((delta > 0 && !invert) || (delta < 0 && invert))
                        PrevPage_Click(null, null);
                    else if ((delta < 0 && !invert) || (delta > 0 && invert))
                        NextPage_Click(null, null);
                    e.Handled = true;
                }
            }
            base.OnPreviewMouseWheel(e);
        }

        // Desplaza dentro de la página actual. Devuelve true si se desplazó, false si ya estaba en el límite.
        private bool ScrollWithinPage(bool down)
        {
            try
            {
                if (_readerScrollViewer == null) return false;
                var sv = _readerScrollViewer;
                // Altura que se puede desplazar
                double maxOffset = Math.Max(0, sv.ScrollableHeight);
                double cur = sv.VerticalOffset;
                if (maxOffset < 0.5)
                {
                    // No hay scroll vertical disponible
                    return false;
                }

                // Paso configurable: proporción del alto visible
                double ratio = SettingsManager.Settings?.PageScrollStepRatio > 0 ? SettingsManager.Settings.PageScrollStepRatio : 0.9;
                double step = Math.Max(24, sv.ViewportHeight * ratio);
                double target = down ? Math.Min(maxOffset, cur + step) : Math.Max(0, cur - step);
                // Si no hay cambio efectivo, estamos en el límite
                if (Math.Abs(target - cur) < 0.5)
                {
                    return false;
                }
                sv.ScrollToVerticalOffset(target);
                return true;
            }
            catch { return false; }
        }

        // Desplaza en modo continuo dentro del ScrollViewer interno.
        // Devuelve true si se produjo desplazamiento.
        private bool ScrollContinuousWithinView(bool down)
        {
            try
            {
                if (_continuousView == null) return false;

                // Primero delegamos a la vista continua que conoce su propio ScrollViewer
                // y puede manejar virtualización (ScrollIntoView + deferred animation).
                try
                {
                    var handled = _continuousView?.ScrollOnePage(down) ?? false;
                    ComicReader.Utils.DevLogger.Debug($"Delegated ScrollOnePage -> {handled}");
                    if (handled) return true;
                }
                catch { }

                // Si la vista no manejó el scroll, intentar usar el ScrollViewer expuesto (si existe)
                var svObj = _continuousView.FindName("ContentScroll") as ScrollViewer;
                if (svObj == null)
                {
                    // No hay ScrollViewer accesible y la vista ya intentó manejarlo -> nada que hacer
                    return false;
                }

                double maxOffset = Math.Max(0, svObj.ScrollableHeight);
                double cur = svObj.VerticalOffset;
                try { ComicReader.Utils.DevLogger.Debug($"ScrollContinuousWithinView called. down={down}, cur={cur}, max={maxOffset}, viewport={svObj.ViewportHeight}"); } catch { }

                // Fallback: usar step basado en viewport (como antes)
                double ratio = SettingsManager.Settings?.PageScrollStepRatio > 0 ? SettingsManager.Settings.PageScrollStepRatio : 0.9;
                double step = Math.Max(24, svObj.ViewportHeight * ratio);
                double target = down ? Math.Min(maxOffset, cur + step) : Math.Max(0, cur - step);
                try { ComicReader.Utils.DevLogger.Debug($"Fallback step. step={step}, target={target}"); } catch { }
                if (Math.Abs(target - cur) < 0.5) return false;
                SmoothScrollTo(svObj, target);
                return true;
            }
            catch { return false; }
        }

        private void FitScreen_Click(object sender, RoutedEventArgs e)
        {
            FitToScreen();
        }

        // Composicion en memoria de dos paginas en una sola imagen para modo
        // doble pagina. Normaliza la altura entre ambas (escala a la mayor) y
        // las pinta una al lado de la otra. El llamador debe pasar `left` y
        // `right` ya en orden de lectura visual (en RTL invertir antes).
        private static System.Windows.Media.Imaging.BitmapSource ComposeDoublePage(
            System.Windows.Media.Imaging.BitmapSource left,
            System.Windows.Media.Imaging.BitmapSource right)
        {
            try
            {
                if (left == null) return right;
                if (right == null) return left;
                int targetH = Math.Max(left.PixelHeight, right.PixelHeight);
                if (targetH <= 0) return left;
                double leftScale = (double)targetH / left.PixelHeight;
                double rightScale = (double)targetH / right.PixelHeight;
                double leftW = left.PixelWidth * leftScale;
                double rightW = right.PixelWidth * rightScale;
                int totalW = (int)Math.Ceiling(leftW + rightW);
                if (totalW <= 0) return left;
                var dv = new System.Windows.Media.DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawImage(left, new System.Windows.Rect(0, 0, leftW, targetH));
                    dc.DrawImage(right, new System.Windows.Rect(leftW, 0, rightW, targetH));
                }
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    totalW, targetH, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();
                return rtb;
            }
            catch { return left; }
        }

        private void FitToScreen()
        {
            if (_currentComicImage?.Parent is ScrollViewer scrollViewer && _currentComicImage.Source != null)
            {
                var imageSource = _currentComicImage.Source;
                double imageWidth = imageSource.Width;
                double imageHeight = imageSource.Height;
                double containerWidth = scrollViewer.ActualWidth - scrollViewer.Padding.Left - scrollViewer.Padding.Right;
                double containerHeight = scrollViewer.ActualHeight - scrollViewer.Padding.Top - scrollViewer.Padding.Bottom;
                // Calcular factor de escala que permita que toda la imagen sea visible
                double scaleX = containerWidth / imageWidth;
                double scaleY = containerHeight / imageHeight;
                
                _zoomFactor = Math.Min(scaleX, scaleY);
                ApplyZoomToImage();
                UpdateZoomIndicator();
                _lastZoomChange = DateTime.UtcNow;
            }
        }

        private void FitToWidth()
        {
            if (_currentComicImage?.Parent is ScrollViewer scrollViewer && _currentComicImage.Source != null)
            {
                var imageSource = _currentComicImage.Source;
                double imageWidth = imageSource.Width;
                double containerWidth = scrollViewer.ActualWidth - scrollViewer.Padding.Left - scrollViewer.Padding.Right;
                
                _zoomFactor = containerWidth / imageWidth;
                ApplyZoomToImage();
                UpdateZoomIndicator();
                _lastZoomChange = DateTime.UtcNow;
            }
        }

        private void ApplyFitToHeight()
        {
            if (_currentComicImage?.Parent is ScrollViewer scrollViewer && _currentComicImage.Source != null)
            {
                var imageSource = _currentComicImage.Source;
                double imageHeight = imageSource.Height;
                
                double containerHeight = scrollViewer.ActualHeight - scrollViewer.Padding.Top - scrollViewer.Padding.Bottom;
                
                _zoomFactor = containerHeight / imageHeight;
                ApplyZoomToImage();
                UpdateZoomIndicator();
                _lastZoomChange = DateTime.UtcNow;
            }
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    OpenComicFile(files[0]);
                }
            }
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (!_isComicOpen) return;

            // Atajos básicos de navegación
            switch (e.Key)
            {
                case Key.Up:
                    try { ComicReader.Utils.DevLogger.Debug("Key.Up pressed"); } catch { }
                    if (SettingsManager.Settings?.EnableContinuousScroll == true)
                    {
                        // En modo continuo: forzar scroll del visor aunque el foco no esté dentro
                        try { var moved = ScrollContinuousWithinView(down: false); ComicReader.Utils.DevLogger.Debug($"Key.Up -> moved={moved}"); if (moved) { e.Handled = true; return; } } catch (Exception ex) { ComicReader.Utils.DevLogger.Debug($"Key.Up exception: {ex}"); }
                        // si no hay desplazamiento posible, dejamos seguir para otras teclas
                    }
                    // Paginado vertical: Up = pagina anterior directa, sin scroll dentro de la pagina.
                    if (SettingsManager.Settings?.VerticalPagedMode == true && SettingsManager.Settings?.EnableContinuousScroll != true)
                    {
                        PrevPage_Click(null, null);
                        e.Handled = true;
                        break;
                    }
                    if (!ScrollWithinPage(down: false))
                    {
                        // Ya está en el tope: ir a página anterior y posicionar al fondo
                        PrevPage_Click(null, null);
                        // Ajustar al final tras cargar
                        this.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (_readerScrollViewer != null)
                                {
                                    _readerScrollViewer.ScrollToVerticalOffset(Math.Max(0, _readerScrollViewer.ScrollableHeight));
                                }
                            }
                            catch { }
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                    e.Handled = true;
                    break;
                case Key.Down:
                    try { ComicReader.Utils.DevLogger.Debug("Key.Down pressed"); } catch { }
                    if (SettingsManager.Settings?.EnableContinuousScroll == true)
                    {
                        // En modo continuo: forzar scroll del visor aunque el foco no esté dentro
                        try { var moved = ScrollContinuousWithinView(down: true); ComicReader.Utils.DevLogger.Debug($"Key.Down -> moved={moved}"); if (moved) { e.Handled = true; return; } } catch (Exception ex) { ComicReader.Utils.DevLogger.Debug($"Key.Down exception: {ex}"); }
                        // si no hay desplazamiento posible, dejamos seguir para otras teclas
                    }
                    // Paginado vertical: Down = pagina siguiente directa.
                    if (SettingsManager.Settings?.VerticalPagedMode == true && SettingsManager.Settings?.EnableContinuousScroll != true)
                    {
                        NextPage_Click(null, null);
                        e.Handled = true;
                        break;
                    }
                    if (!ScrollWithinPage(down: true))
                    {
                        // Ya está al fondo: ir a página siguiente y posicionar arriba
                        NextPage_Click(null, null);
                        this.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try { _readerScrollViewer?.ScrollToVerticalOffset(0); } catch { }
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                    e.Handled = true;
                    break;
                case Key.Left:
                case Key.PageUp:
                case Key.A:
                case Key.K:
                    // En modo continuo: no navegar horizontalmente. Interpretar como scroll vertical hacia arriba si es posible.
                    if (SettingsManager.Settings?.EnableContinuousScroll == true)
                    {
                        try { var moved = ScrollContinuousWithinView(down: false); ComicReader.Utils.DevLogger.Debug($"Key.Left/PageUp -> moved={moved}"); if (moved) { e.Handled = true; break; } } catch (Exception ex) { ComicReader.Utils.DevLogger.Debug($"Key.Left/PageUp exception: {ex}"); }
                        // si no se pudo desplazar verticalmente, no realizar navegación horizontal
                        e.Handled = true; break;
                    }
                    if (SettingsManager.Settings?.CurrentReadingDirection == ReadingDirection.RightToLeft) NextPage_Click(null, null); else PrevPage_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.Right:
                case Key.PageDown:
                case Key.Space:
                case Key.D:
                case Key.J:
                    // En modo continuo: no navegar horizontalmente. Interpretar como scroll vertical hacia abajo si es posible.
                    if (SettingsManager.Settings?.EnableContinuousScroll == true)
                    {
                        try { var moved = ScrollContinuousWithinView(down: true); ComicReader.Utils.DevLogger.Debug($"Key.Right/PageDown/Space -> moved={moved}"); if (moved) { e.Handled = true; break; } } catch (Exception ex) { ComicReader.Utils.DevLogger.Debug($"Key.Right/PageDown/Space exception: {ex}"); }
                        e.Handled = true; break;
                    }
                    // Respetar preferencia de usar barra espaciadora para avanzar
                    if (e.Key != Key.Space || SettingsManager.Settings?.SpacebarNextPage != false)
                    {
                        if (SettingsManager.Settings?.CurrentReadingDirection == ReadingDirection.RightToLeft) PrevPage_Click(null, null); else NextPage_Click(null, null);
                        e.Handled = true;
                    }
                    break;
                case Key.Home:
                    _currentPageIndex = 0;
                    if (SettingsManager.Settings?.EnableContinuousScroll == true && _continuousView != null)
                        _continuousView.ScrollToPage(0);
                    else
                    {
                        Interlocked.Increment(ref _pageLoadSeq);
                        LoadCurrentPage();
                    }
                    UpdatePageIndicator();
                    // Guardar progreso en Home (usar EffectiveProgressOneBased para
                    // que el modo doble pagina reporte la pagina derecha)
                    try { ComicReader.Services.ContinueReadingService.Instance.UpsertProgress(_comicLoader.FilePath, EffectiveProgressOneBased(), _comicLoader.PageCount); } catch { }
                    e.Handled = true;
                    break;
                case Key.End:
                    _currentPageIndex = _comicLoader.Pages.Count - 1;
                    if (SettingsManager.Settings?.EnableContinuousScroll == true && _continuousView != null)
                        _continuousView.ScrollToPage(_currentPageIndex);
                    else
                    {
                        Interlocked.Increment(ref _pageLoadSeq);
                        LoadCurrentPage();
                    }
                    UpdatePageIndicator();
                    // Guardar progreso y eliminar si es ultima pagina
                    try
                    {
                        var oneBased = EffectiveProgressOneBased();
                        ComicReader.Services.ContinueReadingService.Instance.UpsertProgress(_comicLoader.FilePath, oneBased, _comicLoader.PageCount);
                        if (oneBased >= _comicLoader.PageCount)
                        {
                            // UpsertProgress will move it to completed; just refresh UI.
                            _homeView?.RefreshRecent();
                        }
                    }
                    catch { }
                    e.Handled = true;
                    break;
            }

            // Atajos con Ctrl
            if (e.KeyboardDevice.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.OemPlus:
                    case Key.Add:
                        ZoomIn_Click(null, null);
                        e.Handled = true;
                        break;
                    case Key.OemMinus:
                    case Key.Subtract:
                        ZoomOut_Click(null, null);
                        e.Handled = true;
                        break;
                    case Key.D0:
                    case Key.NumPad0:
                        ZoomReset_Click(null, null);
                        e.Handled = true;
                        break;
                    case Key.G:
                        GoToPage_Click(null, null);
                        e.Handled = true;
                        break;
                    case Key.R:
                        Rotate_Click(null, null);
                        e.Handled = true;
                        break;
                    case Key.F:
                        FitToScreen();
                        e.Handled = true;
                        break;
                    case Key.C:
                        // Centrar la página actual en el viewport
                        if (_currentComicImage?.Parent is Grid g && g.Parent is ScrollViewer sv)
                        {
                            var center = (sv.ExtentHeight - sv.ViewportHeight) / 2.0;
                            sv.ScrollToVerticalOffset(Math.Max(0, center));
                        }
                        e.Handled = true;
                        break;
                }
            }

            // Teclas especiales
            switch (e.Key)
            {
                case Key.F12:
                    ToggleImmersiveFullScreen();
                    e.Handled = true;
                    break;
                case Key.N:
                    ToggleNightMode_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.M:
                    ToggleReadingMode_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.S:
                    if (e.KeyboardDevice.Modifiers == ModifierKeys.Shift)
                    {
                        SepiaMode_Click(null, null);
                        e.Handled = true;
                    }
                    break;
                // Shift+C reservado previamente para alto contraste
                case Key.C:
                    if (e.KeyboardDevice.Modifiers == ModifierKeys.Shift)
                    {
                        HighContrastMode_Click(null, null);
                        e.Handled = true;
                    }
                    break;
                case Key.T:
                    ToggleThumbnails_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    if (_isImmersive)
                    {
                        ToggleImmersiveFullScreen();
                        e.Handled = true;
                    }
                    break;
            }
        }

        // Captura previa de teclas para cuando el foco está en zonas en blanco o no interactivas
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Si ya fue manejado por algún control, no hacer nada
            if (e.Handled) return;
            // Reutilizar la lógica principal de teclas para navegación
            MainWindow_KeyDown(sender, e);
        }

        protected override void OnClosed(EventArgs e)
        {
            // Cerrar sesión de lectura si está activa y actualizar historial final
            try
            {
                if (_isComicOpen && _comicLoader != null && !string.IsNullOrEmpty(_comicLoader.FilePath))
                {
                    ComicReader.Services.ContinueReadingService.Instance.UpsertProgress(_comicLoader.FilePath, _currentPageIndex + 1, _comicLoader.PageCount);
                }
            }
            catch { }
            _stats?.EndSession();
            // Persistir estado de ventana al salir
            try
            {
                SettingsManager.Settings.LastWindowState = this.WindowState;
                if (this.WindowState == WindowState.Normal)
                {
                    SettingsManager.Settings.LastWindowWidth = this.ActualWidth;
                    SettingsManager.Settings.LastWindowHeight = this.ActualHeight;
                }
            }
            catch { }
            SettingsManager.SaveSettings();
            base.OnClosed(e);
        }

        // Event Handlers para la barra de título
        private void DragWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                // Doble clic en la barra: alternar maximizar/restaurar
                if (e.ClickCount == 2)
                {
                    MaximizeRestore_Click(sender, e);
                    return;
                }

                // No arrastrar si el click fue sobre un botón u otro control interactivo
                if (e.OriginalSource is DependencyObject dobj)
                {
                    if (FindAncestor<System.Windows.Controls.Button>(dobj) != null)
                        return;
                }

                // Si está maximizada, restaurar y posicionar bajo el cursor para arrastrar
                if (this.WindowState == WindowState.Maximized)
                {
                    // Porcentaje horizontal donde se agarró
                    double percentX = e.GetPosition(this).X / this.ActualWidth;

                    // Posición del cursor en pantalla
                    var screenPoint = PointToScreen(e.GetPosition(this));

                    SaveCurrentWindowPosition(); // guarda valores actuales antes de restaurar
                    RestoreWindowFromMaximized();

                    // Recolocar para que el punto de agarre quede bajo el cursor
                    this.Left = screenPoint.X - this.Width * percentX;
                    this.Top = screenPoint.Y - 10; // pequeño offset hacia arriba
                }

                // Permitir arrastrar
                try { DragMove(); }
                catch { /* ignora errores durante drag */ }
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            try { SystemCommands.MinimizeWindow(this); } catch { }
        }

        private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                try { SystemCommands.RestoreWindow(this); } catch { RestoreWindowFromMaximized(); }
            }
            else
            {
                // Maximizar ventana
                SaveCurrentWindowPosition();
                try { SystemCommands.MaximizeWindow(this); } catch { WindowState = WindowState.Maximized; }
            }
        }

        private void RestoreWindowFromMaximized()
        {
            // Paso 1: Cambiar a estado Normal
            WindowState = WindowState.Normal;
            
            // Paso 2: Forzar valores inmediatamente
            if (_windowStateInitialized && _normalLeft >= 0 && _normalTop >= 0)
            {
                // Aplicar tamaño y posición inmediatamente
                this.Left = _normalLeft;
                this.Top = _normalTop;
                this.Width = _normalWidth;
                this.Height = _normalHeight;
            }
            else
            {
                // Primera vez - centrar
                var workingArea = SystemParameters.WorkArea;
                this.Width = 1000;
                this.Height = 700;
                this.Left = workingArea.Left + (workingArea.Width - this.Width) / 2;
                this.Top = workingArea.Top + (workingArea.Height - this.Height) / 2;
                
                // Guardar estos valores
                _normalLeft = this.Left;
                _normalTop = this.Top;
                _normalWidth = this.Width;
                _normalHeight = this.Height;
                _windowStateInitialized = true;
            }
            
            // Paso 3: Usar UpdateLayout para forzar la actualización
            this.UpdateLayout();
            
            // Paso 4: Aplicar valores nuevamente después del layout
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                this.Left = _normalLeft;
                this.Top = _normalTop;
                this.Width = _normalWidth;
                this.Height = _normalHeight;
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void SaveCurrentWindowPosition()
        {
            if (WindowState == WindowState.Normal)
            {
                _normalLeft = this.Left;
                _normalTop = this.Top;
                _normalWidth = this.Width;
                _normalHeight = this.Height;
                _windowStateInitialized = true;
                
                // Log para debug
                System.Diagnostics.Debug.WriteLine($"Guardado: L={_normalLeft}, T={_normalTop}, W={_normalWidth}, H={_normalHeight}");
            }
        }

        private void RestoreWindowPosition()
        {
            if (_windowStateInitialized && _normalLeft >= 0 && _normalTop >= 0)
            {
                // Verificar que la posición esté dentro de los límites de pantalla
                var workingArea = SystemParameters.WorkArea;
                
                var targetLeft = _normalLeft;
                var targetTop = _normalTop;
                var targetWidth = _normalWidth;
                var targetHeight = _normalHeight;
                
                // Ajustar si está fuera de límites
                if (targetLeft < workingArea.Left) targetLeft = workingArea.Left + 50;
                if (targetTop < workingArea.Top) targetTop = workingArea.Top + 50;
                if (targetLeft + targetWidth > workingArea.Right) targetLeft = workingArea.Right - targetWidth - 50;
                if (targetTop + targetHeight > workingArea.Bottom) targetTop = workingArea.Bottom - targetHeight - 50;
                
                // Aplicar nueva posición y tamaño
                Left = targetLeft;
                Top = targetTop;
                Width = targetWidth;
                Height = targetHeight;
            }
            else
            {
                // Centrar en pantalla si no hay posición guardada
                CenterWindowOnScreen();
            }
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            // Dejar que el sistema gestione el tamaño al maximizar para evitar glitches
            if (WindowState == WindowState.Maximized)
            {
                // Nada extra aquí; XAML y estilos mantienen la barra visible
            }

            // Actualizar icono del botón de maximizar/restaurar
            // Actualizar icono del botón si el elemento existe
            var btn = this.TryFindResource("MaxRestoreButton"); // fallback si no está generado

            // Guardar estado actual en Settings
            try
            {
                SettingsManager.Settings.LastWindowState = this.WindowState;
                if (this.WindowState == WindowState.Normal)
                {
                    SettingsManager.Settings.LastWindowWidth = this.ActualWidth;
                    SettingsManager.Settings.LastWindowHeight = this.ActualHeight;
                }
                SettingsManager.SaveSettings();
            }
            catch { }
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match)
                    return match;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void CenterWindowOnScreen()
        {
            var workingArea = SystemParameters.WorkArea;
            
            // Establecer tamaño razonable
            Width = Math.Min(1000, workingArea.Width * 0.8);
            Height = Math.Min(700, workingArea.Height * 0.8);
            
            // Centrar perfectamente
            Left = workingArea.Left + (workingArea.Width - Width) / 2;
            Top = workingArea.Top + (workingArea.Height - Height) / 2;
            
            // Actualizar variables
            _normalLeft = Left;
            _normalTop = Top;
            _normalWidth = Width;
            _normalHeight = Height;
            _windowStateInitialized = true;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            try { SystemCommands.CloseWindow(this); } catch { Close(); }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            #pragma warning disable CA1416 // WinForms APIs used here (Windows-only)
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Seleccionar carpeta con imágenes de cómic";
            dialog.UseDescriptionForTitle = true;

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                OpenComicFile(dialog.SelectedPath);
            }
            #pragma warning restore CA1416
        }

        

        public void Rotate_Click(object sender, RoutedEventArgs e)
        {
            _rotationAngle = (_rotationAngle + 90) % 360;
            ApplyZoomToImage();
            
            // Ajustar el ajuste después de la rotación para mantener la imagen visible
            if (_rotationAngle == 90 || _rotationAngle == 270)
            {
                // En 90° y 270°, la imagen está rotada, ajustar según el modo actual
                var currentFitMode = GetCurrentFitMode();
                if (currentFitMode == "width")
                {
                    ApplyFitToHeight(); // Cambiar a fit height cuando rotamos
                }
                else if (currentFitMode == "height")
                {
                    FitToWidth(); // Cambiar a fit width cuando rotamos
                }
            }
        }

        public void RotateLeft_Click(object sender, RoutedEventArgs e)
        {
            _rotationAngle = (_rotationAngle - 90 + 360) % 360;
            ApplyZoomToImage();
        }

        public void RotateReset_Click(object sender, RoutedEventArgs e)
        {
            _rotationAngle = 0;
            ApplyZoomToImage();
        }

        private string GetCurrentFitMode()
        {
            // Determinar el modo de ajuste actual basado en el zoom factor
            if (_currentComicImage?.Parent is ScrollViewer scrollViewer && _currentComicImage.Source != null)
            {
                var imageSource = _currentComicImage.Source;
                double containerWidth = scrollViewer.ActualWidth;
                double containerHeight = scrollViewer.ActualHeight;
                
                double widthRatio = containerWidth / imageSource.Width;
                double heightRatio = containerHeight / imageSource.Height;
                
                if (Math.Abs(_zoomFactor - widthRatio) < 0.01)
                    return "width";
                else if (Math.Abs(_zoomFactor - heightRatio) < 0.01)
                    return "height";
                else if (Math.Abs(_zoomFactor - Math.Min(widthRatio, heightRatio)) < 0.01)
                    return "screen";
            }
            return "custom";
        }

        public void ToggleNightMode_Click(object sender, RoutedEventArgs e)
        {
            _isNightMode = !_isNightMode;
            SettingsManager.Settings.IsNightMode = _isNightMode;
            SettingsManager.SaveSettings();
            ApplyReadingModeEffects();
        }

        public void ToggleReadingMode_Click(object sender, RoutedEventArgs e)
        {
            _isReadingMode = !_isReadingMode;
            SettingsManager.Settings.IsReadingMode = _isReadingMode;
            SettingsManager.SaveSettings();
            ApplyReadingModeEffects();
        }

        public void SepiaMode_Click(object sender, RoutedEventArgs e)
        {
            ToggleSepiaMode();
        }

        public void HighContrastMode_Click(object sender, RoutedEventArgs e)
        {
            ToggleHighContrastMode();
        }

        private bool _sepiaMode = false;
        private bool _highContrastMode = false;

        private void ToggleSepiaMode()
        {
            _sepiaMode = !_sepiaMode;
            ApplyReadingModeEffects();
        }

        private void ToggleHighContrastMode()
        {
            _highContrastMode = !_highContrastMode;
            ApplyReadingModeEffects();
        }

        private void ApplyReadingModeEffects()
        {
            if (_currentComicImage == null) return;

            // Implementación simplificada de efectos usando opacidad y filtros básicos
            var transformGroup = new System.Windows.Media.TransformGroup();
            
            // Aplicar zoom y rotación existentes
            transformGroup.Children.Add(new System.Windows.Media.ScaleTransform(_zoomFactor, _zoomFactor));
            if (_rotationAngle != 0)
            {
                transformGroup.Children.Add(new System.Windows.Media.RotateTransform(_rotationAngle));
            }
            
            _currentComicImage.RenderTransform = transformGroup;

            // Cambiar el fondo según el modo usando colores básicos
            if (_isNightMode)
            {
                this.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 20, 20));
                _currentComicImage.Opacity = 1.0;
            }
            else if (_sepiaMode)
            {
                this.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 230, 210));
                _currentComicImage.Opacity = 1.0;
            }
            else
            {
                this.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
                _currentComicImage.Opacity = 1.0;
                _currentComicImage.Effect = null;
            }
        }

        

    // Eliminado: soporte de pantalla completa estándar (solo se mantiene modo inmersivo)

        private void EnsureReaderInputFocus()
        {
            try
            {
                // Priorizar enfocar el ScrollViewer del lector si existe
                if (_readerScrollViewer != null)
                {
                    _readerScrollViewer.Focus();
                }
                else
                {
                    this.Focus();
                }
                this.Activate();
                Keyboard.Focus(_readerScrollViewer ?? (IInputElement)this);
            }
            catch { }
        }

        // Eliminado: ToggleFullScreen y UI asociada

        private void ToggleThumbnails_Click(object sender, RoutedEventArgs e)
        {
            _thumbnailsVisible = !_thumbnailsVisible;
            var panel = this.FindName("ThumbPanel") as FrameworkElement;
            var col = this.FindName("ThumbCol") as System.Windows.Controls.ColumnDefinition;
            var list = this.FindName("ThumbList") as System.Windows.Controls.ListBox;
            if (_thumbnailsVisible)
            {
                // Mostrar el panel de inmediato
                if (panel != null) panel.Visibility = Visibility.Visible;
                if (col != null) col.Width = new GridLength(260);
                if (list != null)
                {
                    // Forzar rebind limpiando primero
                    _suppressThumbListSelectionChange = true;
                    try
                    {
                        list.ItemsSource = null;
                        list.Items.Clear();
                        list.ItemsSource = _comicLoader?.Pages;
                        list.SelectedIndex = _currentPageIndex;
                        list.ScrollIntoView(list.SelectedItem);
                    }
                    finally { _suppressThumbListSelectionChange = false; }
                    // Cargar miniaturas en segundo plano para no bloquear la apertura del panel
                    long startSeq = Interlocked.Increment(ref _thumbLoadSeq);
                    var loaderRef = _comicLoader;
                    var token = _ctsWindow.Token;
                    var _thumbsTask = Task.Run(async () =>
                    {
                        try
                        {
                            int count = loaderRef?.Pages?.Count ?? 0;
                            int maxDegree = Math.Max(2, Math.Min(Environment.ProcessorCount, 6));
                            using var gate = new System.Threading.SemaphoreSlim(maxDegree);
                            var tasks = Enumerable.Range(0, count).Select(async i =>
                            {
                                if (token.IsCancellationRequested) return;
                                await gate.WaitAsync(token).ConfigureAwait(false);
                                try
                                {
                                    if (token.IsCancellationRequested) return;
                                    var thumb = await loaderRef.GetPageThumbnailAsync(i, 180, 240, token).ConfigureAwait(false);
                                    var idx = i;
                                    this.Dispatcher.Invoke(() =>
                                    {
                                        // Evitar escribir si cambió el cómic o la secuencia
                                        if (token.IsCancellationRequested) return;
                                        if (!ReferenceEquals(_comicLoader, loaderRef)) return;
                                        if (Interlocked.Read(ref _thumbLoadSeq) != startSeq) return;
                                        if (idx >= 0 && idx < _comicLoader.Pages.Count)
                                            _comicLoader.Pages[idx].Thumbnail = thumb;
                                    });
                                }
                                finally { try { gate.Release(); } catch { } }
                            }).ToArray();
                            try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { }
                        }
                        catch { }
                    }, token);
                    TrackBackgroundTask(_thumbsTask);
                }
                try { SettingsManager.Settings.ThumbnailsVisible = true; SettingsManager.SaveSettings(); } catch { }
            }
            else
            {
                if (panel != null) panel.Visibility = Visibility.Collapsed;
                if (col != null) col.Width = new GridLength(0);
                if (list != null)
                {
                    list.ItemsSource = null;
                    list.SelectedIndex = -1;
                }
                // Invalida cargas de miniaturas en curso
                try { Interlocked.Increment(ref _thumbLoadSeq); } catch { }
                try { SettingsManager.Settings.ThumbnailsVisible = false; SettingsManager.SaveSettings(); } catch { }
            }
        }

        private void ThumbList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressThumbListSelectionChange) return;
            if (!_isComicOpen || _comicLoader.Pages.Count == 0) return;
            var list = sender as System.Windows.Controls.ListBox;
            if (list?.SelectedIndex >= 0 && list.SelectedIndex < _comicLoader.Pages.Count)
            {
                _currentPageIndex = list.SelectedIndex;
                LoadCurrentPage();
                UpdatePageIndicator();
            }
        }

        

        // Métodos públicos para acceso desde otras vistas
        public void OpenComicAsync(string filePath, int initialPage = 0)
        {
            OpenComicFile(filePath);
            if (_comicLoader.Pages.Count > initialPage && initialPage >= 0)
            {
                _currentPageIndex = initialPage;
                if (SettingsManager.Settings?.EnableContinuousScroll == true && _continuousView != null)
                {
                    _continuousView.ScrollToPage(_currentPageIndex);
                }
                else
                {
                    Interlocked.Increment(ref _pageLoadSeq);
                    LoadCurrentPage();
                }
                try { ComicReader.Services.ContinueReadingService.Instance.UpsertProgress(_comicLoader.FilePath, _currentPageIndex + 1, _comicLoader.PageCount); } catch { }
            }
        }

        // --- Overlay Auto-Hide ---
        private System.Windows.Threading.DispatcherTimer _overlayHideTimer;
        private bool _overlayVisible = true;
        private readonly TimeSpan _overlayIdleDelay = TimeSpan.FromSeconds(2.5);
        private readonly Duration _overlayFadeDuration = new Duration(TimeSpan.FromMilliseconds(280));
        private FrameworkElement _readerOverlay;
        private System.Windows.Threading.DispatcherTimer _autoAdvanceTimer;
    // Cursor auto-oculto en modo inmersivo
    private System.Windows.Threading.DispatcherTimer _cursorHideTimer;
    private bool _cursorHidden = false;
    private TimeSpan _cursorIdleDelay = TimeSpan.FromSeconds(2.5);

        private FrameworkElement GetOverlayElement()
        {
            var ov = this.FindName("ReaderOverlay") as FrameworkElement;
            if (ov == null)
                ov = this.FindName("ReaderTopBar") as FrameworkElement;
            return ov;
        }

        private void EnsureOverlayBehavior()
        {
            if (_readerOverlay == null)
            {
                _readerOverlay = GetOverlayElement();
                if (_readerOverlay != null)
                {
                    _readerOverlay.Opacity = 0.96; // visible inicial
                    _readerOverlay.IsHitTestVisible = true;
                }
            }
            if (_overlayHideTimer == null)
            {
                var secs = SettingsManager.Settings?.HideOverlayDelaySeconds;
                var delay = secs.HasValue && secs.Value > 0 ? TimeSpan.FromSeconds(secs.Value) : _overlayIdleDelay;
                _overlayHideTimer = new System.Windows.Threading.DispatcherTimer { Interval = delay };
                _overlayHideTimer.Tick += (_, __) =>
                {
                    // Respetar preferencia de ocultar solo en inmersivo
                    if (SettingsManager.Settings?.HideOverlayOnlyInImmersive == true && !_isImmersive)
                        return;
                    HideReaderOverlay();
                };
                _overlayHideTimer.Start();
            }
            this.MouseMove -= MainWindow_MouseMoveForOverlay;
            this.MouseMove += MainWindow_MouseMoveForOverlay;
        }

                // Eliminado: lógica antigua de historial reemplazada por ContinueReadingService

        private void MainWindow_MouseMoveForOverlay(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Si se configuró ocultar overlay solo en inmersivo, no hacer nada en modo normal
            if (SettingsManager.Settings?.HideOverlayOnlyInImmersive == true && !_isImmersive)
                return;
            if (!_overlayVisible)
            {
                ShowReaderOverlay();
            }
            ResetOverlayTimer();
            if (_isImmersive)
            {
                if (_cursorHidden && !_isPanning)
                {
                    this.Cursor = Cursors.Arrow;
                    _cursorHidden = false;
                }
                if (_cursorHideTimer != null)
                {
                    _cursorHideTimer.Stop();
                    _cursorHideTimer.Start();
                }
            }
        }

        private void ResetOverlayTimer()
        {
            if (_overlayHideTimer == null) return;
            _overlayHideTimer.Stop();
            var secs = SettingsManager.Settings?.HideOverlayDelaySeconds;
            var delay = secs.HasValue && secs.Value > 0 ? TimeSpan.FromSeconds(secs.Value) : _overlayIdleDelay;
            _overlayHideTimer.Interval = delay;
            _overlayHideTimer.Start();
        }

        private void ShowReaderOverlay()
        {
            if (_readerOverlay == null) return;
            _overlayVisible = true;
            _readerOverlay.IsHitTestVisible = true;
            AnimateOverlayOpacity(_readerOverlay, _readerOverlay.Opacity, 0.96);
        }

        private void HideReaderOverlay()
        {
            if (_readerOverlay == null) return;
            _overlayVisible = false;
            AnimateOverlayOpacity(_readerOverlay, _readerOverlay.Opacity, 0.0, () =>
            {
                if (!_overlayVisible && _readerOverlay != null)
                {
                    _readerOverlay.IsHitTestVisible = false;
                }
            });
        }

        private void AnimateOverlayOpacity(UIElement element, double from, double to, Action completed = null)
        {
            var fade = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = from,
                To = to,
                Duration = _overlayFadeDuration,
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };
            if (completed != null)
            {
                fade.Completed += (_, __) => completed();
            }
            element.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        // Hook overlay después de cargar la ventana
        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            // Mantener compatibilidad si existe ReaderOverlay, si no, no hacer nada
            _readerOverlay = GetOverlayElement();
            EnsureOverlayBehavior();
        }

        private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (SettingsManager.Settings?.EnableZoomPan == true && _readerScrollViewer != null && CanPan())
            {
                _isPanning = true;
                _panStartPoint = e.GetPosition(this);
                _panStartVerticalOffset = _readerScrollViewer.VerticalOffset;
                _panStartHorizontalOffset = _readerScrollViewer.HorizontalOffset;
                _currentComicImage.CaptureMouse();
                this.Cursor = Cursors.Hand;
                e.Handled = true;
            }
        }

        private void Image_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                _currentComicImage.ReleaseMouseCapture();
                this.Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        }

        private void Image_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning && _readerScrollViewer != null)
            {
                var pos = e.GetPosition(this);
                var dy = pos.Y - _panStartPoint.Y;
                var dx = pos.X - _panStartPoint.X;
                _readerScrollViewer.ScrollToVerticalOffset(_panStartVerticalOffset - dy);
                _readerScrollViewer.ScrollToHorizontalOffset(_panStartHorizontalOffset - dx);
            }
        }

        private void Image_MouseEnter(object sender, MouseEventArgs e)
        {
            if (SettingsManager.Settings?.EnableZoomPan == true && CanPan())
            {
                this.Cursor = Cursors.Hand;
            }
        }

        private void Image_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isPanning)
            {
                this.Cursor = Cursors.Arrow;
            }
        }

        private bool CanPan()
        {
            if (_readerScrollViewer == null) return false;
            bool canH = _readerScrollViewer.ScrollableWidth > 0.5;
            bool canV = _readerScrollViewer.ScrollableHeight > 0.5;
            return canH || canV || Math.Abs(_zoomFactor - 1.0) > 0.01;
        }

        public void ImmersiveFullScreen_Click(object sender, RoutedEventArgs e)
        {
            ToggleImmersiveFullScreen();
        }

        private void ToggleImmersiveFullScreen()
        {
            if (_immersiveTransitionBusy) return; // evitar reentrada
            _immersiveTransitionBusy = true;
            try
            {
            if (!_isImmersive)
            {
                // Guardar estado
                _savedWindowStyle = this.WindowStyle;
                _savedResizeMode = this.ResizeMode;
                _savedWindowState = this.WindowState;
                _savedBackgroundBrush = this.Background;
                _savedTopmost = this.Topmost;
                // Guardar bounds si estamos en ventana normal
                if (this.WindowState == WindowState.Normal)
                {
                    _savedLeft = this.Left;
                    _savedTop = this.Top;
                    _savedWidth = this.Width;
                    _savedHeight = this.Height;
                }

                var titleBar = this.FindName("CustomTitleBar") as FrameworkElement;
                var topBar = this.FindName("ReaderTopBar") as FrameworkElement;
                var thumbPanel = this.FindName("ThumbPanel") as FrameworkElement;
                var thumbCol = this.FindName("ThumbCol") as System.Windows.Controls.ColumnDefinition;

                // Ocultar overlay si existe (ReaderOverlay o ReaderTopBar)
                var ov = GetOverlayElement();
                if (ov != null)
                {
                    _savedOverlayOpacity = ov.Opacity;
                    _savedOverlayHit = ov.IsHitTestVisible;
                    ov.Opacity = 0.0;
                    ov.IsHitTestVisible = false;
                }
                var pi = this.FindName("PageIndicator") as FrameworkElement;
                if (pi != null) pi.Visibility = Visibility.Collapsed;

                // Entrar en pantalla completa total (cubrir taskbar) usando bounds del monitor actual
                this.WindowStyle = WindowStyle.None;
                this.ResizeMode = ResizeMode.NoResize;
                var hwnd = new WindowInteropHelper(this).Handle;
                #pragma warning disable CA1416 // WinForms Screen API (Windows-only)
                var screen = WinForms.Screen.FromHandle(hwnd);
                var bounds = screen.Bounds;
                #pragma warning restore CA1416
                this.Topmost = true;
                this.WindowState = WindowState.Normal; // necesario para aplicar tamaño exacto
                // Convertir de píxeles a DIPs para WPF (DPI-aware)
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                double scaleX = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
                double scaleY = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;
                this.Left = bounds.Left / scaleX;
                this.Top = bounds.Top / scaleY;
                this.Width = Math.Max(100, bounds.Width / scaleX);
                this.Height = Math.Max(100, bounds.Height / scaleY);
                this.Background = System.Windows.Media.Brushes.Black;
                if (titleBar != null)
                {
                    _savedTitleBarVisibility = titleBar.Visibility;
                    titleBar.Visibility = Visibility.Collapsed;
                }
                if (topBar != null)
                {
                    _savedTopBarVisibility = topBar.Visibility;
                    topBar.Visibility = Visibility.Collapsed;
                }
                if (thumbPanel != null)
                {
                    _savedThumbPanelVisibility = thumbPanel.Visibility;
                    thumbPanel.Visibility = Visibility.Collapsed;
                }
                if (thumbCol != null)
                {
                    _savedThumbColWidth = thumbCol.Width;
                    _savedThumbColWidthSet = true;
                    thumbCol.Width = new GridLength(0);
                }
                // Ocultar barra de desplazamiento para no distraer
                if (_readerScrollViewer != null)
                {
                    _savedVerticalScrollBarVisibility = _readerScrollViewer.VerticalScrollBarVisibility;
                    _savedHorizontalScrollBarVisibility = _readerScrollViewer.HorizontalScrollBarVisibility;
                    _readerScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                    _readerScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
                }
                // Mejorar calidad de escalado
                if (_currentComicImage != null)
                {
                    _savedImageScalingMode = System.Windows.Media.RenderOptions.GetBitmapScalingMode(_currentComicImage);
                    System.Windows.Media.RenderOptions.SetBitmapScalingMode(_currentComicImage, System.Windows.Media.BitmapScalingMode.HighQuality);
                }
                _isImmersive = true;
                // Asegurar foco para que las teclas y la rueda funcionen
                EnsureReaderInputFocus();
                // Iniciar temporizador para ocultar cursor
                // Actualizar delay desde Settings
                var cursorSecs = SettingsManager.Settings?.HideCursorDelaySeconds;
                _cursorIdleDelay = (cursorSecs.HasValue && cursorSecs.Value > 0) ? TimeSpan.FromSeconds(cursorSecs.Value) : TimeSpan.FromSeconds(2.5);
                if (_cursorHideTimer == null)
                {
                    _cursorHideTimer = new System.Windows.Threading.DispatcherTimer { Interval = _cursorIdleDelay };
                    _cursorHideTimer.Tick += (_, __) =>
                    {
                        if (_isImmersive && !_isPanning)
                        {
                            this.Cursor = Cursors.None;
                            _cursorHidden = true;
                        }
                        _cursorHideTimer.Stop();
                    };
                }
                _cursorHideTimer.Stop();
                _cursorHideTimer.Interval = _cursorIdleDelay;
                _cursorHideTimer.Start();
                // Mantener pantalla activa mientras está inmersivo
                TryKeepDisplayAwake(true);

                // Fundido suave si está activado
                if (SettingsManager.Settings?.FadeOnFullscreenTransitions == true)
                {
                    try
                    {
                        var fade = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            From = 0.0,
                            To = 1.0,
                            Duration = new Duration(TimeSpan.FromMilliseconds(160))
                        };
                        this.BeginAnimation(Window.OpacityProperty, fade);
                    }
                    catch { }
                }
            }
            else
            {
                // Restaurar overlay (ReaderOverlay o ReaderTopBar)
                var ov = GetOverlayElement();
                if (ov != null)
                {
                    ov.Opacity = _savedOverlayOpacity;
                    ov.IsHitTestVisible = _savedOverlayHit;
                    _overlayVisible = ov.Opacity > 0.01;
                }
                var pi = this.FindName("PageIndicator") as FrameworkElement;
                if (pi != null)
                {
                    pi.Visibility = (SettingsManager.Settings?.ShowPageNumberOverlay == true) ? Visibility.Visible : Visibility.Collapsed;
                }

                // Salir de pantalla completa inmersiva
                this.WindowStyle = _savedWindowStyle;
                this.ResizeMode = _savedResizeMode;
                // Restaurar tamaño/posición si se entró desde ventana normal
                if (_savedWindowState == WindowState.Normal)
                {
                    this.Topmost = _savedTopmost;
                    this.WindowState = WindowState.Normal;
                    if (!double.IsNaN(_savedLeft)) this.Left = _savedLeft;
                    if (!double.IsNaN(_savedTop)) this.Top = _savedTop;
                    if (!double.IsNaN(_savedWidth)) this.Width = _savedWidth;
                    if (!double.IsNaN(_savedHeight)) this.Height = _savedHeight;
                }
                else
                {
                    // Si era Maximize/otro estado, restaurar directamente
                    this.WindowState = _savedWindowState;
                    this.Topmost = _savedTopmost;
                }
                this.Background = _savedBackgroundBrush ?? this.Background;

                var titleBar = this.FindName("CustomTitleBar") as FrameworkElement;
                var topBar = this.FindName("ReaderTopBar") as FrameworkElement;
                var thumbPanel = this.FindName("ThumbPanel") as FrameworkElement;
                var thumbCol = this.FindName("ThumbCol") as System.Windows.Controls.ColumnDefinition;
                // Restaurar elementos visuales y layout ocultos en inmersivo
                if (titleBar != null)
                {
                    titleBar.Visibility = _savedTitleBarVisibility;
                }
                if (topBar != null)
                {
                    topBar.Visibility = _savedTopBarVisibility;
                }
                if (thumbPanel != null)
                {
                    thumbPanel.Visibility = _savedThumbPanelVisibility;
                }
                if (thumbCol != null && _savedThumbColWidthSet)
                {
                    thumbCol.Width = _savedThumbColWidth;
                }

                // Restaurar elementos de desplazamiento y escalado
                if (_readerScrollViewer != null)
                {
                    _readerScrollViewer.VerticalScrollBarVisibility = _savedVerticalScrollBarVisibility;
                    _readerScrollViewer.HorizontalScrollBarVisibility = _savedHorizontalScrollBarVisibility;
                }
                if (_currentComicImage != null)
                {
                    System.Windows.Media.RenderOptions.SetBitmapScalingMode(_currentComicImage, _savedImageScalingMode);
                    try { _currentComicImage.ReleaseMouseCapture(); } catch { }
                    _isPanning = false;
                }
                _isImmersive = false;
                // Restaurar cursor y detener timer
                if (_cursorHideTimer != null)
                {
                    _cursorHideTimer.Stop();
                }
                this.Cursor = Cursors.Arrow;
                _cursorHidden = false;
                // Volver a la política de energía normal
                TryKeepDisplayAwake(false);

                // Reanudar overlay/timers y aplicar efectos visuales del modo lectura
                EnsureOverlayBehavior();
                ApplyReadingModeEffects();
                // Reasegurar foco de lectura tras el cambio
                EnsureReaderInputFocus();
                // Forzar un relayout después de cambiar chrome/estado
                try { this.UpdateLayout(); this.InvalidateVisual(); } catch { }

                // Fundido al restaurar si está activado
                if (SettingsManager.Settings?.FadeOnFullscreenTransitions == true)
                {
                    try
                    {
                        var fade = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            From = 0.0,
                            To = 1.0,
                            Duration = new Duration(TimeSpan.FromMilliseconds(160))
                        };
                        this.BeginAnimation(Window.OpacityProperty, fade);
                    }
                    catch { }
                }
            }
            }
            finally
            {
                _immersiveTransitionBusy = false;
            }
        }

        private void EnsureAutoAdvanceBehavior()
        {
            if (_autoAdvanceTimer == null)
            {
                _autoAdvanceTimer = new System.Windows.Threading.DispatcherTimer();
                _autoAdvanceTimer.Tick += (s, e) =>
                {
                    if (!_isComicOpen || _comicLoader.Pages.Count == 0) return;
                    if (!SettingsManager.Settings?.AutoAdvancePages ?? true) return;
                    // Pausar autoavance si está interactuando el usuario
                    if (_isPanning || (DateTime.UtcNow - _lastZoomChange) < _interactionGrace) return;
                    // En doble pagina con conteo par, el ultimo par tiene
                    // _currentPageIndex == Pages.Count - 2, pero NextPage_Click
                    // hace snap a inicio de par y retorna early sin avanzar.
                    // Detectamos "fin de comic" verificando si el indice cambio,
                    // no solo por la condicion (idx < count-1).
                    bool advanced = false;
                    if (_currentPageIndex < _comicLoader.Pages.Count - 1)
                    {
                        int before = _currentPageIndex;
                        NextPage_Click(null, null);
                        advanced = _currentPageIndex != before;
                    }
                    if (!advanced)
                    {
                        if (SettingsManager.Settings?.AutoAdvanceLoop == true)
                        {
                            _currentPageIndex = 0;
                            LoadCurrentPage();
                            UpdatePageIndicator();
                        }
                        else
                        {
                            _autoAdvanceTimer?.Stop();
                        }
                    }
                };
            }

            var intervalSec = Math.Max(1, SettingsManager.Settings?.AutoAdvanceInterval ?? 5);
            _autoAdvanceTimer.Interval = TimeSpan.FromSeconds(intervalSec);

            if (SettingsManager.Settings?.AutoAdvancePages == true)
            {
                _autoAdvanceTimer.Start();
            }
            else
            {
                _autoAdvanceTimer.Stop();
            }
        }

        // Permite aplicar cambios de configuración en caliente cuando el usuario pulsa "Aplicar" en el diálogo
        public void ApplySettingsRuntime()
        {
            try
            {
                _comicLoader?.RefreshTuningFromSettings();
                EnsureAutoAdvanceBehavior();
                ResetOverlayTimer();
                // Actualizar timers con nuevos valores
                var secs = SettingsManager.Settings?.HideOverlayDelaySeconds;
                if (_overlayHideTimer != null)
                {
                    var newInterval = (secs.HasValue && secs.Value > 0) ? TimeSpan.FromSeconds(secs.Value) : _overlayHideTimer.Interval;
                    _overlayHideTimer.Interval = newInterval;
                }
                var cursorSecs = SettingsManager.Settings?.HideCursorDelaySeconds;
                if (_cursorHideTimer != null)
                {
                    var newInterval = (cursorSecs.HasValue && cursorSecs.Value > 0) ? TimeSpan.FromSeconds(cursorSecs.Value) : _cursorHideTimer.Interval;
                    _cursorHideTimer.Interval = newInterval;
                }
                _isNightMode = SettingsManager.Settings?.IsNightMode == true;
                _isReadingMode = SettingsManager.Settings?.IsReadingMode == true;
                ApplyReadingModeEffects();

                // Si hay un cómic abierto, asegurar que el modo de lectura actual refleja las preferencias (scroll continuo vs página única)
                if (_isComicOpen)
                {
                    bool wantContinuous = SettingsManager.Settings?.EnableContinuousScroll == true;
                    bool isContinuous = CurrentView == _continuousView;
                    if (wantContinuous != isContinuous)
                    {
                        // Cambiar de vista respetando el cómic cargado
                        ShowComicView();
                    }
                    else if (!wantContinuous)
                    {
                        // Re-aplicar el ajuste inicial cuando estamos en modo no continuo
                        var mode = SettingsManager.Settings?.DefaultFitMode?.ToLowerInvariant();
                        switch (mode)
                        {
                            case "height":
                                ApplyFitToHeight();
                                break;
                            case "screen":
                                FitToScreen();
                                break;
                            case "original":
                                _zoomFactor = 1.0; ApplyZoomToImage(); UpdateZoomIndicator();
                                break;
                            case "width":
                            default:
                                FitToWidth();
                                break;
                        }
                        // Reaplicar brillo/contraste en la imagen actual
                        TryApplyBrightnessContrastToCurrentPageImage();
                    }

                    // En modo continuo, reaplicar configuración visual si es necesario
                    if (wantContinuous)
                    {
                        try
                        {
                            // Reaplicar brillo/contraste en elementos visibles
                            _continuousView?.ReapplyBrightnessContrastVisible();
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Inicializa los servicios premium: Notificaciones, Validación, Errores, Carga
        /// </summary>
        private void InitializePremiumServices()
        {
            try
            {
                // Inicializar sistema de notificaciones
                ComicReader.Services.Notifications.NotificationService.Instance.Initialize(this);
                
                // Inicializar sistema de carga
                ComicReader.Services.Loading.LoadingService.Instance.Initialize(this);
                
                // Mostrar notificación de bienvenida
                ComicReader.Services.Notifications.NotificationService.Instance.Success(
                    "Todos los sistemas listos", 
                    "Percy's Library", 
                    2000
                );
            }
            catch (Exception ex)
            {
                // Fallback si falla la inicialización
                try
                {
                    ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(
                        ex, 
                        "Inicialización de servicios premium",
                        ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Silent
                    );
                }
                catch { }
            }
        }

        private void TryApplyBrightnessContrastToCurrentPageImage()
        {
            try
            {
                if (_currentComicImage == null) return;
                var s = SettingsManager.Settings;
                if (s == null) return;
                if (_comicLoader == null || _comicLoader.Pages == null || _currentPageIndex < 0 || _currentPageIndex >= _comicLoader.Pages.Count) return;
                var page = _comicLoader.Pages[_currentPageIndex];
                var baseImage = page?.Image; // asumimos que es la imagen original (BitmapImage)
                if (baseImage == null) return;
                if (Math.Abs(s.Brightness - 1.0) < 0.001 && Math.Abs(s.Contrast - 1.0) < 0.001)
                {
                    _currentComicImage.Source = baseImage;
                    return;
                }
                var adjusted = ImageAdjuster.ApplyBrightnessContrast(baseImage, s.Brightness, s.Contrast);
                _currentComicImage.Source = adjusted;
            }
            catch { }
        }

        // Mantener la pantalla activa en modo inmersivo (evita que se apague/atenué)
        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);
        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;
        private void TryKeepDisplayAwake(bool enable)
        {
            try
            {
                if (enable)
                {
                    SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED);
                }
                else
                {
                    SetThreadExecutionState(ES_CONTINUOUS);
                }
            }
            catch { }
        }
    }
}