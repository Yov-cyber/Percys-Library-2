using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using ComicReader.Models;

namespace ComicReader.Services
{
    public enum ReadingMode
    {
        SinglePage,
        DoublePage,
        ContinuousScroll,
        FitToWidth,
        FitToHeight
    }

    public enum ZoomMode
    {
        FitToWindow,
        FitToWidth,
        FitToHeight,
        ActualSize,
        Custom
    }

    public enum ThemeMode
    {
        Light,
        Dark,
        Comic,
        Sepia,
        HighContrast,

        // Marvel-inspired / high energy palettes
        StarkRed,
        PatriotBlue,
        ArañaRoja,
        GammaGreen,
        AsgardGold,

        // DC-inspired / heroic palettes
        BatNight,
        KryptonBlue,
        MetroNeon,
        AmazonEmerald,
        OracleGray,

        // Manga styles
        ShonenBurst,
        ShojoBloom,
        SeinenNoir,
        GekigaSepia,
        MangaInk,

        // Classic comic ages & styles
        GoldenAge,
        SilverAge,
        BronzeAge,
        PopArt,
        Pulps,

        // Retro / modern hybrids
        NoirStrip,
        PastelRetro,
        NeonCyber,
        Vaporwave,
        Retro80s,

        // Misc stylistic palettes
        ComicPop,
        VintagePaper,
        CelShade,
        CartoonBright,
        MonochromeHighContrast,
        PastelGentle,
        // Test theme
        TestHotPink,
        // Exclusive app-branded theme
        PercysLibrary
    }

    public class AdvancedSettings : INotifyPropertyChanged
    {
        // Lectura y Visualización
        private ReadingMode _readingMode = ReadingMode.SinglePage;
        private ZoomMode _defaultZoomMode = ZoomMode.FitToWindow;
        private double _customZoomLevel = 1.0;
        private bool _smoothScrolling = true;
        private bool _invertScrollDirection = false;
        private double _scrollSpeed = 1.0;

        // Interfaz y Tema
        private ThemeMode _currentTheme = ThemeMode.Comic;
        private bool _fullScreenHidesUI = true;
        private bool _showPageCounter = true;
        private bool _showProgressBar = true;
        private double _uiOpacity = 1.0;
        private bool _animatePageTransitions = true;

        // Archivos y Biblioteca
        private ObservableCollection<string> _recentFolders = new ObservableCollection<string>();
        private ObservableCollection<string> _favoriteFormats = new ObservableCollection<string>();
        private bool _rememberLastPosition = true;
        private bool _autoOpenLastComic = false;
        private int _maxRecentItems = 20;

        // Rendimiento
        private int _preloadPages = 3;
        private int _cacheSize = 100; // MB
        private bool _useHardwareAcceleration = true;
        private bool _preloadThumbnails = true;
        private int _maxThumbnailSize = 200;

        // Controles y Accesos Directos
        private bool _mouseWheelZooms = true;
        private bool _doubleClickFullscreen = true;
        private bool _spacebarNextPage = true;
        private bool _arrowKeysNavigate = true;

        public event PropertyChangedEventHandler PropertyChanged;

        // Propiedades de Lectura y Visualización
        public ReadingMode ReadingMode
        {
            get => _readingMode;
            set { _readingMode = value; OnPropertyChanged(nameof(ReadingMode)); }
        }

        public ZoomMode DefaultZoomMode
        {
            get => _defaultZoomMode;
            set { _defaultZoomMode = value; OnPropertyChanged(nameof(DefaultZoomMode)); }
        }

        public double CustomZoomLevel
        {
            get => _customZoomLevel;
            set { _customZoomLevel = Math.Max(0.1, Math.Min(5.0, value)); OnPropertyChanged(nameof(CustomZoomLevel)); }
        }

        public bool SmoothScrolling
        {
            get => _smoothScrolling;
            set { _smoothScrolling = value; OnPropertyChanged(nameof(SmoothScrolling)); }
        }

        public bool InvertScrollDirection
        {
            get => _invertScrollDirection;
            set { _invertScrollDirection = value; OnPropertyChanged(nameof(InvertScrollDirection)); }
        }

        public double ScrollSpeed
        {
            get => _scrollSpeed;
            set { _scrollSpeed = Math.Max(0.1, Math.Min(3.0, value)); OnPropertyChanged(nameof(ScrollSpeed)); }
        }

        // Propiedades de Interfaz y Tema
        public ThemeMode CurrentTheme
        {
            get => _currentTheme;
            set { _currentTheme = value; OnPropertyChanged(nameof(CurrentTheme)); }
        }

        public bool FullScreenHidesUI
        {
            get => _fullScreenHidesUI;
            set { _fullScreenHidesUI = value; OnPropertyChanged(nameof(FullScreenHidesUI)); }
        }

        public bool ShowPageCounter
        {
            get => _showPageCounter;
            set { _showPageCounter = value; OnPropertyChanged(nameof(ShowPageCounter)); }
        }

        public bool ShowProgressBar
        {
            get => _showProgressBar;
            set { _showProgressBar = value; OnPropertyChanged(nameof(ShowProgressBar)); }
        }

        public double UIOpacity
        {
            get => _uiOpacity;
            set { _uiOpacity = Math.Max(0.1, Math.Min(1.0, value)); OnPropertyChanged(nameof(UIOpacity)); }
        }

        public bool AnimatePageTransitions
        {
            get => _animatePageTransitions;
            set { _animatePageTransitions = value; OnPropertyChanged(nameof(AnimatePageTransitions)); }
        }

        // Propiedades de Archivos y Biblioteca
        public ObservableCollection<string> RecentFolders
        {
            get => _recentFolders;
            set { _recentFolders = value; OnPropertyChanged(nameof(RecentFolders)); }
        }

        public ObservableCollection<string> FavoriteFormats
        {
            get => _favoriteFormats;
            set { _favoriteFormats = value; OnPropertyChanged(nameof(FavoriteFormats)); }
        }

        public bool RememberLastPosition
        {
            get => _rememberLastPosition;
            set { _rememberLastPosition = value; OnPropertyChanged(nameof(RememberLastPosition)); }
        }

        public bool AutoOpenLastComic
        {
            get => _autoOpenLastComic;
            set { _autoOpenLastComic = value; OnPropertyChanged(nameof(AutoOpenLastComic)); }
        }

        public int MaxRecentItems
        {
            get => _maxRecentItems;
            set { _maxRecentItems = Math.Max(5, Math.Min(50, value)); OnPropertyChanged(nameof(MaxRecentItems)); }
        }

        // Propiedades de Rendimiento
        public int PreloadPages
        {
            get => _preloadPages;
            set { _preloadPages = Math.Max(1, Math.Min(10, value)); OnPropertyChanged(nameof(PreloadPages)); }
        }

        public int CacheSize
        {
            get => _cacheSize;
            set { _cacheSize = Math.Max(50, Math.Min(1000, value)); OnPropertyChanged(nameof(CacheSize)); }
        }

        public bool UseHardwareAcceleration
        {
            get => _useHardwareAcceleration;
            set { _useHardwareAcceleration = value; OnPropertyChanged(nameof(UseHardwareAcceleration)); }
        }

        public bool PreloadThumbnails
        {
            get => _preloadThumbnails;
            set { _preloadThumbnails = value; OnPropertyChanged(nameof(PreloadThumbnails)); }
        }

        public int MaxThumbnailSize
        {
            get => _maxThumbnailSize;
            set { _maxThumbnailSize = Math.Max(100, Math.Min(400, value)); OnPropertyChanged(nameof(MaxThumbnailSize)); }
        }

        // Propiedades de Controles
        public bool MouseWheelZooms
        {
            get => _mouseWheelZooms;
            set { _mouseWheelZooms = value; OnPropertyChanged(nameof(MouseWheelZooms)); }
        }

        public bool DoubleClickFullscreen
        {
            get => _doubleClickFullscreen;
            set { _doubleClickFullscreen = value; OnPropertyChanged(nameof(DoubleClickFullscreen)); }
        }

        public bool SpacebarNextPage
        {
            get => _spacebarNextPage;
            set { _spacebarNextPage = value; OnPropertyChanged(nameof(SpacebarNextPage)); }
        }

        public bool ArrowKeysNavigate
        {
            get => _arrowKeysNavigate;
            set { _arrowKeysNavigate = value; OnPropertyChanged(nameof(ArrowKeysNavigate)); }
        }

        public AdvancedSettings()
        {
            InitializeDefaults();
        }

        private void InitializeDefaults()
        {
            // Inicializar formatos favoritos
            FavoriteFormats.Add("cbz");
            FavoriteFormats.Add("cbr");
            FavoriteFormats.Add("pdf");
            FavoriteFormats.Add("epub");
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void ResetToDefaults()
        {
            ReadingMode = ReadingMode.SinglePage;
            DefaultZoomMode = ZoomMode.FitToWindow;
            CustomZoomLevel = 1.0;
            SmoothScrolling = true;
            InvertScrollDirection = false;
            ScrollSpeed = 1.0;
            CurrentTheme = ThemeMode.Comic;
            FullScreenHidesUI = true;
            ShowPageCounter = true;
            ShowProgressBar = true;
            UIOpacity = 1.0;
            AnimatePageTransitions = true;
            RememberLastPosition = true;
            AutoOpenLastComic = false;
            MaxRecentItems = 20;
            PreloadPages = 3;
            CacheSize = 100;
            UseHardwareAcceleration = true;
            PreloadThumbnails = true;
            MaxThumbnailSize = 200;
            MouseWheelZooms = true;
            DoubleClickFullscreen = true;
            SpacebarNextPage = true;
            ArrowKeysNavigate = true;
        }
    }
}