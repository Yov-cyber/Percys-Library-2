using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using ComicReader.Models;

namespace ComicReader.Legacy.Services
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

    /// <summary>
    /// Temas disponibles para Percy's Library
    /// NOTA: Todos los temas están inspirados en la estética de superhéroes
    /// pero usan nombres genéricos libres de copyright
    /// </summary>
    public enum ThemeMode
    {
        // ═══════════════════════════════════════════════════════════════════
        // TEMAS BÁSICOS
        // ═══════════════════════════════════════════════════════════════════
        Light,
        Dark,
        Comic,
        Sepia,
        HighContrast,

        // ═══════════════════════════════════════════════════════════════════
        // TEMAS INSPIRADOS EN SUPERHÉROES (Genéricos - Sin Copyright)
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>Armor Red - Rojo metálico y dorado (estilo tech hero)</summary>
        ArmorRed,
        
        /// <summary>Patriot Shield - Azul, rojo y blanco (estilo héroe patriótico)</summary>
        PatriotShield,
        
        /// <summary>Web Crawler - Rojo y negro (estilo héroe arácnido)</summary>
        WebCrawler,
        
        /// <summary>Gamma Rage - Verde radiactivo (estilo héroe con fuerza)</summary>
        GammaRage,
        
        /// <summary>Thunder God - Dorado y azul eléctrico (estilo dios del trueno)</summary>
        ThunderGod,
        
        /// <summary>Dark Knight - Negro y gris oscuro (estilo vigilante nocturno)</summary>
        DarkKnight,
        
        /// <summary>Kryptonian Blue - Azul y rojo brillante (estilo héroe alienígena)</summary>
        KryptonianBlue,
        
        /// <summary>Speed Force - Rojo y amarillo con energía (estilo velocista)</summary>
        SpeedForce,
        
        /// <summary>Amazon Warrior - Rojo, azul y dorado (estilo guerrera amazona)</summary>
        AmazonWarrior,
        
        /// <summary>Emerald Lantern - Verde brillante (estilo portador de anillo)</summary>
        EmeraldLantern,
        
        /// <summary>Ocean King - Verde azulado y naranja (estilo rey marino)</summary>
        OceanKing,
        
        /// <summary>Cyber Warrior - Gris metálico y azul (estilo cyborg)</summary>
        CyberWarrior,
        
        /// <summary>Scarlet Speedster - Carmesí vibrante (estilo corredor escarlata)</summary>
        ScarletSpeedster,
        
        /// <summary>Emerald Archer - Verde bosque y negro (estilo arquero)</summary>
        EmeraldArcher,
        
        /// <summary>Feline Burglar - Negro y morado (estilo ladrona felina)</summary>
        FelineBurglar,
        
        /// <summary>Mercenary Red - Rojo y negro (estilo mercenario)</summary>
        MercenaryRed,
        
        /// <summary>Mystic Arts - Naranja místico y azul (estilo hechicero)</summary>
        MysticArts,
        
        /// <summary>Panther King - Negro y morado real (estilo rey felino)</summary>
        PantherKing,

        // ═══════════════════════════════════════════════════════════════════
        // TEMAS MANGA Y ANIME
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>Shonen Burst - Colores vibrantes para acción</summary>
        ShonenBurst,
        
        /// <summary>Shojo Bloom - Pasteles rosados para romance</summary>
        ShojoBloom,
        
        /// <summary>Seinen Noir - Oscuro y maduro</summary>
        SeinenNoir,
        
        /// <summary>Gekiga Sepia - Tonos sepia dramáticos</summary>
        GekigaSepia,
        
        /// <summary>Manga Ink - Blanco y negro puro</summary>
        MangaInk,

        // ═══════════════════════════════════════════════════════════════════
        // TEMAS DE ERAS DE CÓMICS
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>Golden Age - Era dorada (1938-1956)</summary>
        GoldenAge,
        
        /// <summary>Silver Age - Era plateada (1956-1970)</summary>
        SilverAge,
        
        /// <summary>Bronze Age - Era bronce (1970-1985)</summary>
        BronzeAge,
        
        /// <summary>Modern Age - Era moderna (1985+)</summary>
        ModernAge,

        // ═══════════════════════════════════════════════════════════════════
        // TEMAS ARTÍSTICOS
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>Pop Art - Estilo Andy Warhol</summary>
        PopArt,
        
        /// <summary>Noir Strip - Blanco y negro con alto contraste</summary>
        NoirStrip,
        
        /// <summary>Pulp Fiction - Estilo revistas pulp</summary>
        PulpFiction,
        
        /// <summary>Cel Shading - Colores planos tipo animación</summary>
        CelShading,
        
        /// <summary>Watercolor - Acuarela suave</summary>
        Watercolor,

        // ═══════════════════════════════════════════════════════════════════
        // TEMAS RETRO Y MODERNOS
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>Neon Cyber - Neón y cyberpunk</summary>
        NeonCyber,
        
        /// <summary>Vaporwave - Estética retro-futurista</summary>
        Vaporwave,
        
        /// <summary>Retro 80s - Colores vibrantes de los 80</summary>
        Retro80s,
        
        /// <summary>Pastel Retro - Pasteles vintage</summary>
        PastelRetro,
        
        /// <summary>Synthwave - Neón y atardeceres</summary>
        Synthwave,

        // ═══════════════════════════════════════════════════════════════════
        // TEMAS ESPECIALES
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>Vintage Paper - Papel envejecido</summary>
        VintagePaper,
        
        /// <summary>Cartoon Bright - Colores brillantes de caricatura</summary>
        CartoonBright,
        
        /// <summary>Monochrome High Contrast - Blanco y negro puro</summary>
        MonochromeHighContrast,
        
        /// <summary>Pastel Gentle - Pasteles suaves</summary>
        PastelGentle,
        
        /// <summary>Comic Pop - Colores pop vibrantes</summary>
        ComicPop,
        
        /// <summary>Percy's Library - Tema oficial de la aplicación</summary>
        PercysLibrary,
        
        /// <summary>Test Theme - Solo para pruebas de persistencia</summary>
        TestHotPink
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