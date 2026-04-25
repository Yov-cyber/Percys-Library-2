using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ComicReader.Services
{
    public class AppSettings : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }

        private string _theme = "Dark";
        public string Theme { get => _theme; set => SetProperty(ref _theme, value); }

        private bool _thumbnailsVisible = true;
        public bool ThumbnailsVisible { get => _thumbnailsVisible; set => SetProperty(ref _thumbnailsVisible, value); }

        private bool _isReadingMode = false;
        public bool IsReadingMode { get => _isReadingMode; set => SetProperty(ref _isReadingMode, value); }

        private bool _isNightMode = false;
        public bool IsNightMode { get => _isNightMode; set => SetProperty(ref _isNightMode, value); }

        private bool _rememberLastSession = true;
        public bool RememberLastSession { get => _rememberLastSession; set => SetProperty(ref _rememberLastSession, value); }

        private bool _autoFitOnLoad = true;
        public bool AutoFitOnLoad { get => _autoFitOnLoad; set => SetProperty(ref _autoFitOnLoad, value); }

        private bool _enableZoomPan = true;
        public bool EnableZoomPan { get => _enableZoomPan; set => SetProperty(ref _enableZoomPan, value); }

        private bool _showPageNumberOverlay = true;
        public bool ShowPageNumberOverlay { get => _showPageNumberOverlay; set => SetProperty(ref _showPageNumberOverlay, value); }

        private bool _showLoadingIndicators = true;
        public bool ShowLoadingIndicators { get => _showLoadingIndicators; set => SetProperty(ref _showLoadingIndicators, value); }

        // Performance / prefetch tuning
    // Habilitar precarga completa por defecto para mejorar experiencia: evita imágenes borrosas al navegar.
    private bool _enableEagerPreload = true;
    public bool EnableEagerPreload { get => _enableEagerPreload; set => SetProperty(ref _enableEagerPreload, value); }

        private int _eagerPreloadConcurrency = 3;
        public int EagerPreloadConcurrency { get => _eagerPreloadConcurrency; set => SetProperty(ref _eagerPreloadConcurrency, value); }

    // Mantener en memoria las páginas precargadas por defecto para evitar swaps borrosos.
    private bool _enableEagerPreloadInMemory = true;
    public bool EnableEagerPreloadInMemory { get => _enableEagerPreloadInMemory; set => SetProperty(ref _enableEagerPreloadInMemory, value); }

    // Pre-cargar TODAS las páginas en modo uno a uno (elimina imágenes borrosas completamente)
    private bool _preloadAllPagesInSinglePageMode = true;
    public bool PreloadAllPagesInSinglePageMode { get => _preloadAllPagesInSinglePageMode; set => SetProperty(ref _preloadAllPagesInSinglePageMode, value); }

    // Controlar si las animaciones están habilitadas en toda la aplicación
    private bool _animationsEnabled = true;
    public bool AnimationsEnabled { get => _animationsEnabled; set => SetProperty(ref _animationsEnabled, value); }

    // Por defecto permitir un número razonable de páginas en RAM; el usuario puede ajustarlo en Settings.
    private int _eagerPreloadMemoryLimitPages = 50;
    public int EagerPreloadMemoryLimitPages { get => _eagerPreloadMemoryLimitPages; set => SetProperty(ref _eagerPreloadMemoryLimitPages, value); }

        private long _eagerPreloadDiskMaxBytes = 100_000_000;
        public long EagerPreloadDiskMaxBytes { get => _eagerPreloadDiskMaxBytes; set => SetProperty(ref _eagerPreloadDiskMaxBytes, value); }

        private int _eagerPreloadDiskMaxFiles = 100;
        public int EagerPreloadDiskMaxFiles { get => _eagerPreloadDiskMaxFiles; set => SetProperty(ref _eagerPreloadDiskMaxFiles, value); }

        // Cache / loader tuning
        private int _pageCacheLimit = 3;
        public int PageCacheLimit { get => _pageCacheLimit; set => SetProperty(ref _pageCacheLimit, value); }

        private int _prefetchWindow = 2;
        public int PrefetchWindow { get => _prefetchWindow; set => SetProperty(ref _prefetchWindow, value); }

        private int _concurrencyCap = 4;
        public int ConcurrencyCap { get => _concurrencyCap; set => SetProperty(ref _concurrencyCap, value); }

        private int _pdfRenderWidth = 1200;
        public int PdfRenderWidth { get => _pdfRenderWidth; set => SetProperty(ref _pdfRenderWidth, value); }

        private int _pdfRenderHeight = 1600;
        public int PdfRenderHeight { get => _pdfRenderHeight; set => SetProperty(ref _pdfRenderHeight, value); }

        // UI preferences
        private bool _isRecentListView = true;
        public bool IsRecentListView { get => _isRecentListView; set => SetProperty(ref _isRecentListView, value); }

        // Backwards-compatible aliases and UI fields used by SettingsWindow.xaml
    private bool _autoSaveEnabled = true;
        public bool AutoSaveEnabled { get => _autoSaveEnabled; set => SetProperty(ref _autoSaveEnabled, value); }

        private bool _openLastOnStartup = false;
        public bool OpenLastOnStartup { get => _openLastOnStartup; set => SetProperty(ref _openLastOnStartup, value); }

        // Theme alias (keeps existing Theme property as single source of truth)
        public string ThemeName { get => Theme; set => Theme = value; }

        private string _accentColorName = "Rojo";
        public string AccentColorName { get => _accentColorName; set => SetProperty(ref _accentColorName, value); }

        private string _readerFontName = "Segoe UI";
        public string ReaderFontName { get => _readerFontName; set => SetProperty(ref _readerFontName, value); }

        private string _uiScale = "Medium";
        public string UIScale { get => _uiScale; set => SetProperty(ref _uiScale, value); }

        private bool _enableLivePreview = false;
        public bool EnableLivePreview { get => _enableLivePreview; set => SetProperty(ref _enableLivePreview, value); }

        private int _defaultZoomPercent = 100;
        public int DefaultZoomPercent { get => _defaultZoomPercent; set => SetProperty(ref _defaultZoomPercent, value); }

        // Map to ShowPageNumberOverlay for compatibility
        public bool ShowPageNumbers { get => ShowPageNumberOverlay; set => ShowPageNumberOverlay = value; }

        private bool _touchNavigation = true;
        public bool TouchNavigation { get => _touchNavigation; set => SetProperty(ref _touchNavigation, value); }

        private double _scrollSensitivity = 1.0;
        public double ScrollSensitivity { get => _scrollSensitivity; set => SetProperty(ref _scrollSensitivity, value); }

        // Convenience MB setter for existing EagerPreloadDiskMaxBytes
        public int EagerPreloadDiskMaxMB
        {
            get => (int)Math.Max(0, EagerPreloadDiskMaxBytes / (1024 * 1024));
            set => EagerPreloadDiskMaxBytes = Math.Max(0L, (long)value * 1024 * 1024);
        }

        private bool _pinLockEnabled = false;
        public bool PINLockEnabled { get => _pinLockEnabled; set => SetProperty(ref _pinLockEnabled, value); }

        private bool _hidePrivateComics = false;
        public bool HidePrivateComics { get => _hidePrivateComics; set => SetProperty(ref _hidePrivateComics, value); }

    // Preferencia de vista para la sección "Seguir leyendo": "Carousel", "List" o "Grid"
    private string _continueViewMode = "Carousel";
    public string ContinueViewMode { get => _continueViewMode; set => SetProperty(ref _continueViewMode, value); }

    // Nota: la opción HomeViewTheme se eliminó; use la propiedad global 'Theme' para controlar el tema de la aplicación.
        private double _brightness = 1.0;
        public double Brightness { get => _brightness; set => SetProperty(ref _brightness, value); }

        private double _contrast = 1.0;
        public double Contrast { get => _contrast; set => SetProperty(ref _contrast, value); }

        private double _pageScrollStepRatio = 0.9;
        public double PageScrollStepRatio { get => _pageScrollStepRatio; set => SetProperty(ref _pageScrollStepRatio, value); }

        private string _readerBackgroundCustomPath = null;
        public string ReaderBackgroundCustomPath { get => _readerBackgroundCustomPath; set => SetProperty(ref _readerBackgroundCustomPath, value); }

        private string _defaultComicsFolder = null;
        public string DefaultComicsFolder { get => _defaultComicsFolder; set => SetProperty(ref _defaultComicsFolder, value); }

        private double _vignetteBorderThickness = 0.0;
        public double VignetteBorderThickness { get => _vignetteBorderThickness; set => SetProperty(ref _vignetteBorderThickness, value); }

        private bool _enableComicEffects = true;
        public bool EnableComicEffects { get => _enableComicEffects; set => SetProperty(ref _enableComicEffects, value); }

    // Order of modules in the Reading Statistics window (persisted)
    private string[] _readingStatsModuleOrder = null;
    public string[] ReadingStatsModuleOrder { get => _readingStatsModuleOrder; set => SetProperty(ref _readingStatsModuleOrder, value); }

    // Concurrency limit for generating reading-stats thumbnails in background
    private int _readingStatsThumbConcurrency = 2;
    public int ReadingStatsThumbConcurrency { get => _readingStatsThumbConcurrency; set => SetProperty(ref _readingStatsThumbConcurrency, value); }

        // Thumbnail cache limits (disk-backed thumbnails used across the app)
        private int _thumbCacheMaxFiles = 500;
        public int ThumbCacheMaxFiles { get => _thumbCacheMaxFiles; set => SetProperty(ref _thumbCacheMaxFiles, value); }

        private long _thumbCacheMaxBytes = 200 * 1024 * 1024; // 200 MB
        public long ThumbCacheMaxBytes { get => _thumbCacheMaxBytes; set => SetProperty(ref _thumbCacheMaxBytes, value); }

        // Convenience MB view for binding in Settings UI
        public int ThumbCacheMaxMB
        {
            get => (int)Math.Max(0, ThumbCacheMaxBytes / (1024 * 1024));
            set => ThumbCacheMaxBytes = Math.Max(0L, (long)value * 1024 * 1024);
        }

        // Last opened progress/state
        private string _lastOpenedFilePath = null;
        public string LastOpenedFilePath { get => _lastOpenedFilePath; set => SetProperty(ref _lastOpenedFilePath, value); }

        private int _lastOpenedPage = 0;
        public int LastOpenedPage { get => _lastOpenedPage; set => SetProperty(ref _lastOpenedPage, value); }

        // Logging / diagnostics
        private bool _enablePerfLogs = false;
        public bool EnablePerfLogs { get => _enablePerfLogs; set => SetProperty(ref _enablePerfLogs, value); }

        private bool _enableDeveloperLogs = false;
        public bool EnableDeveloperLogs { get => _enableDeveloperLogs; set => SetProperty(ref _enableDeveloperLogs, value); }

        // Reader behaviour
        private bool _enableContinuousScroll = false;
        public bool EnableContinuousScroll { get => _enableContinuousScroll; set => SetProperty(ref _enableContinuousScroll, value); }

        private string _defaultFitMode = "width";
        public string DefaultFitMode { get => _defaultFitMode; set => SetProperty(ref _defaultFitMode, value); }

        private double _lastWindowWidth = 1000.0;
        public double LastWindowWidth { get => _lastWindowWidth; set => SetProperty(ref _lastWindowWidth, value); }

        private double _lastWindowHeight = 700.0;
        public double LastWindowHeight { get => _lastWindowHeight; set => SetProperty(ref _lastWindowHeight, value); }

        private WindowState _lastWindowState = WindowState.Normal;
        public WindowState LastWindowState { get => _lastWindowState; set => SetProperty(ref _lastWindowState, value); }

        private bool _autoEnterImmersiveOnOpen = false;
        public bool AutoEnterImmersiveOnOpen { get => _autoEnterImmersiveOnOpen; set => SetProperty(ref _autoEnterImmersiveOnOpen, value); }

        private double _hideOverlayDelaySeconds = 3.0;
        public double HideOverlayDelaySeconds { get => _hideOverlayDelaySeconds; set => SetProperty(ref _hideOverlayDelaySeconds, value); }

        // Default false: la barra del lector se auto-oculta tambien en modo
        // ventana (no solo en pantalla completa inmersiva). Es la experiencia
        // "UI invisible" que esperamos por defecto.
        private bool _hideOverlayOnlyInImmersive = false;
        public bool HideOverlayOnlyInImmersive { get => _hideOverlayOnlyInImmersive; set => SetProperty(ref _hideOverlayOnlyInImmersive, value); }

        // Phase 3: flag de migracion. Cuando es false, SettingsManager.LoadSettings()
        // resetea HideOverlayOnlyInImmersive a false una vez para usuarios que
        // venian del layout viejo donde la barra tenia su propia fila. Despues
        // de migrar se setea a true y nunca mas se toca.
        private bool _overlayLayoutMigrationApplied = false;
        public bool OverlayLayoutMigrationApplied { get => _overlayLayoutMigrationApplied; set => SetProperty(ref _overlayLayoutMigrationApplied, value); }

        private double _hideCursorDelaySeconds = 3.0;
        public double HideCursorDelaySeconds { get => _hideCursorDelaySeconds; set => SetProperty(ref _hideCursorDelaySeconds, value); }

        private bool _fadeOnFullscreenTransitions = true;
        public bool FadeOnFullscreenTransitions { get => _fadeOnFullscreenTransitions; set => SetProperty(ref _fadeOnFullscreenTransitions, value); }

        private bool _autoAdvancePages = false;
        public bool AutoAdvancePages { get => _autoAdvancePages; set => SetProperty(ref _autoAdvancePages, value); }

        private bool _autoAdvanceLoop = false;
        public bool AutoAdvanceLoop { get => _autoAdvanceLoop; set => SetProperty(ref _autoAdvanceLoop, value); }

        private int _autoAdvanceInterval = 5;
        public int AutoAdvanceInterval { get => _autoAdvanceInterval; set => SetProperty(ref _autoAdvanceInterval, value); }

        private bool _invertScrollWheel = false;
        public bool InvertScrollWheel { get => _invertScrollWheel; set => SetProperty(ref _invertScrollWheel, value); }

        private bool _spacebarNextPage = true;
        public bool SpacebarNextPage { get => _spacebarNextPage; set => SetProperty(ref _spacebarNextPage, value); }

        private ReadingDirection _currentReadingDirection = ReadingDirection.LeftToRight;
        public ReadingDirection CurrentReadingDirection { get => _currentReadingDirection; set => SetProperty(ref _currentReadingDirection, value); }

        // Doble pagina: cuando true y EnableContinuousScroll=false, el lector
        // muestra dos paginas consecutivas lado a lado. La pareja se construye
        // por composicion (RenderTargetBitmap), respetando manga RTL.
        private bool _doublePageEnabled = false;
        public bool DoublePageEnabled { get => _doublePageEnabled; set => SetProperty(ref _doublePageEnabled, value); }

        // Paginado vertical estricto: cuando true y EnableContinuousScroll=false,
        // las flechas Arriba/Abajo cambian de pagina (en lugar de hacer scroll
        // dentro de la pagina). Es la experiencia "single-page vertical" sin
        // scroll continuo.
        private bool _verticalPagedMode = false;
        public bool VerticalPagedMode { get => _verticalPagedMode; set => SetProperty(ref _verticalPagedMode, value); }

        // Additional properties for Settings UI compatibility
        private string _readingMode = "PageByPage";
        public string ReadingMode { get => _readingMode; set => SetProperty(ref _readingMode, value); }

        private string _pageTurnAnimation = "Fade";
        public string PageTurnAnimation { get => _pageTurnAnimation; set => SetProperty(ref _pageTurnAnimation, value); }

        // Toque humano: nombre del usuario, opcional. Se usa para personalizar
        // el saludo en el header de Home segun la hora del dia. Si esta vacio,
        // el header muestra simplemente "Biblioteca".
        private string _userName = string.Empty;
        public string UserName { get => _userName; set => SetProperty(ref _userName, value ?? string.Empty); }

        // ComicVine API: integracion opt-in para enriquecer metadatos de los
        // comics (titulo, numero, fecha, sinopsis, creditos). Requiere registro
        // gratuito en https://comicvine.gamespot.com/api/ y respeta su rate
        // limit (~200 req/hora). Si la key esta vacia o el flag esta apagado,
        // la app funciona 100% offline como hasta ahora.
        private string _comicVineApiKey = string.Empty;
        public string ComicVineApiKey { get => _comicVineApiKey; set => SetProperty(ref _comicVineApiKey, value ?? string.Empty); }

        private bool _enableComicVineEnrichment = false;
        public bool EnableComicVineEnrichment { get => _enableComicVineEnrichment; set => SetProperty(ref _enableComicVineEnrichment, value); }

        // Add more stubs here as needed by other parts of the app.
    }
}
