// FileName: /Services/ComicPageLoader.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Threading;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.Tar; // Para CBT
using SharpCompress.Archives.SevenZip; // Para CB7
using SharpCompress.Common; // Para PasswordProtectedException
using SharpCompress.Common.Rar; // Para PasswordProtectedException específico
using ComicReader.Models;
using System.Drawing; // Para Bitmap
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Globalization;
using VersOne.Epub; // Para EPUB (requiere NuGet: VersOne.Epub)
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
#if SUPPORT_DJVU
using DjvuNet; // Para DJVU (requiere NuGet: DjvuNet)
#endif
#if SUPPORT_PDF
using Docnet.Core; // Render PDF
using Docnet.Core.Models;
using Docnet.Core.Readers;
#endif
// PDF support will be conditional based on availability
// using SixLabors.ImageSharp; // Para WebP/HEIC (requiere NuGet: SixLabors.ImageSharp.WebP, SixLabors.ImageSharp.Heif)
// using SixLabors.ImageSharp.Processing; // Para redimensionar imágenes

using ComicReader.Core.Abstractions;
using ComicReader.Core.Services;
// duplicate using removed
using ComicReader.Core.Adapters;
namespace ComicReader.Services
{
    public partial class ComicPageLoader : IDisposable, IComicPageLoader
    {
        public event System.Action<int, System.Windows.Media.Imaging.BitmapImage> FullImageReady;

        // Renderer prioritizado para renderizado de imágenes
        private readonly ComicReader.Core.Rendering.PrioritizedRenderer _renderer = new ComicReader.Core.Rendering.PrioritizedRenderer();

        // Cancellation token source para operaciones en curso (se renueva al cargar/limpiar comic)
        private CancellationTokenSource _internalCts = new CancellationTokenSource();

        private string _filePath;
        private List<Models.ComicPage> _pages = new List<Models.ComicPage>();
        private readonly ComicReader.ContinuousReader.CacheManager<int, (BitmapImage img, DateTime ts)> _pageCache = new(200); // OPTIMIZADO: Más páginas en cache
        private readonly ComicReader.ContinuousReader.CacheManager<int, (BitmapImage img, DateTime ts)> _thumbCache = new(400); // OPTIMIZADO: Más miniaturas
        // Inicial: permitir hasta 4 hilos de prefetch en máquinas con >4 cores, pero mínimo 1
        private System.Threading.SemaphoreSlim _prefetchSemaphore = new(System.Math.Max(1, System.Math.Min(4, Environment.ProcessorCount)));
        private readonly ConcurrentDictionary<int, Task<BitmapImage>> _ongoingPageLoads = new();
        private const int MaxDecodeWidth = 2000; // OPTIMIZADO: Mayor resolución para pantallas modernas
        // Semaphore to limit concurrent decodes (configurable)
        private System.Threading.SemaphoreSlim _decodeSemaphore = new(System.Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2)));
        // Metrics
        private double _lastSwapMs = 0;

        public int PageCacheCount => _pageCache.Count;
        public int ThumbCacheCount => _thumbCache.Count;
        public int OngoingLoadsCount => _ongoingPageLoads.Count;
        public double LastSwapMs => _lastSwapMs;
        public int PrefetchWindow => _prefetchWindow;

        public void SetPrefetchWindow(int newWindow)
        {
            try
            {
                var w = Math.Max(1, Math.Min(16, newWindow));
                lock (_lock)
                {
                    _prefetchWindow = w;
                    // cap is proportional to window but bounded to avoid excessive concurrency
                            try { SetConcurrencyCap(SettingsManager.Settings.ConcurrencyCap); } catch (Exception ex) { Logger.LogException("ApplySettingsParameters SetConcurrencyCap failed", ex); }
                    int cap = Math.Max(1, Math.Min(6, (int)Math.Ceiling(w * 0.5)));
                    try { _prefetchSemaphore = new System.Threading.SemaphoreSlim(cap); } catch (Exception ex) { Logger.LogException("Failed to create prefetch semaphore", ex); }
                }
                _log?.Log($"Prefetch window set to {w}", LogLevel.Info);
            }
            catch (Exception ex) { Logger.LogException("ClearCaches failed", ex); }
        }

        // Allow external tuning of the concurrency cap for image decodes
        public void SetConcurrencyCap(int cap)
        {
            try
            {
                int c = Math.Max(1, Math.Min(16, cap));
                // replace semaphore (best-effort)
                var old = _decodeSemaphore;
                _decodeSemaphore = new System.Threading.SemaphoreSlim(c);
                try { old?.Dispose(); } catch (Exception ex) { Logger.LogException("Failed disposing old decode semaphore", ex); }
                _log?.Log($"Decode concurrency cap set to {c}", LogLevel.Info);
            }
            catch (Exception ex) { Logger.LogException("SetPrefetchWindow failed", ex); }
        }
        // Track when a quick thumbnail was placed into page cache to measure swap latency
        private readonly ConcurrentDictionary<int, DateTime> _quickCachedAt = new();
        // Limita lecturas concurrentes de archivos comprimidos para evitar saturar I/O y posibles deadlocks
        private static readonly System.Threading.SemaphoreSlim _archiveSemaphore = new(3);
        private int _pageCacheLimit = 60; // configurable luego
        private readonly object _lruLock = new();
        private ILogService _log;
    // Track currently pinned page indices to avoid repeated pin/unpin churn
    private readonly HashSet<int> _currentlyPinnedPages = new HashSet<int>();
    private readonly ComicReader.Core.Abstractions.IImageCache _multiLevelCache;
        private int _prefetchWindow = 2;
        private object _lock = new object(); // Para sincronizar acceso a recursos compartidos
        // Tipo real del archivo comprimido detectado por firma para manejar CBR mal renombrados
        private ArchiveKind _archiveKind = ArchiveKind.None;

        // Documentos cargados para formatos especiales
#if SUPPORT_PDF
        private IDocReader _pdfDocument;
#endif
#if SUPPORT_DJVU
        private DjvuDocument _djvuDocument;
#endif

        public List<Models.ComicPage> Pages => _pages;
        public string ComicTitle => string.IsNullOrEmpty(_filePath) ? "Cómic sin título" : Path.GetFileNameWithoutExtension(_filePath);
        public int PageCount => _pages.Count;
        public string FilePath => _filePath ?? "";

        public ComicPageLoader()
        {
            InitLog();
            _log?.Log("Initializing ComicPageLoader (empty constructor)");
            ApplySettingsParameters();
            try { SetPrefetchWindow(_prefetchWindow); } catch { }
            _multiLevelCache = ComicReader.Core.Services.ServiceLocator.TryGet<ComicReader.Core.Abstractions.IImageCache>();
        }

        public ComicPageLoader(string filePath)
        {
            _filePath = filePath;
            InitLog();
            _log?.Log($"Initializing ComicPageLoader for: {_filePath}");
            ApplySettingsParameters();
            _multiLevelCache = ComicReader.Core.Services.ServiceLocator.TryGet<ComicReader.Core.Abstractions.IImageCache>();
        }

        // Limpia el cómic actual (ruta y páginas) sin disponer la instancia
        public void ClearCurrent()
        {
            try
            {
#if SUPPORT_PDF
                if (_pdfDocument != null) { try { _pdfDocument.Dispose(); } catch { } _pdfDocument = null; }
#endif
#if SUPPORT_DJVU
                if (_djvuDocument != null) { try { _djvuDocument.Dispose(); } catch { } _djvuDocument = null; }
#endif
            }
            catch { }
            _filePath = null;
            _archiveKind = ArchiveKind.None;
            try { _pages.Clear(); } catch { }
            try { _pageCache.Clear(); } catch { }
            try { _thumbCache.Clear(); } catch { }
            try
            {
                // Cancel any in-flight operations for the previous comic
                try { _internalCts?.Cancel(); } catch { }
                try { _internalCts?.Dispose(); } catch { }
            }
            catch { }
            // Create a fresh token source for subsequent loads
            _internalCts = new CancellationTokenSource();
        }

        private void InitLog()
        {
            _log = ServiceLocator.TryGet<ILogService>() ?? new LogServiceAdapter();
        }

        private void ApplySettingsParameters()
        {
            try
            {
                if (SettingsManager.Settings != null)
                {
                    if (SettingsManager.Settings.PageCacheLimit > 0) _pageCacheLimit = SettingsManager.Settings.PageCacheLimit;
                    if (SettingsManager.Settings.PrefetchWindow > 0) _prefetchWindow = SettingsManager.Settings.PrefetchWindow;
                    try { SetConcurrencyCap(SettingsManager.Settings.ConcurrencyCap); } catch { }
                }
            }
            catch { }
        }

        // Permitir actualizar parámetros en caliente
        public void RefreshTuningFromSettings()
        {
            ApplySettingsParameters();
            EnforcePageCacheLimit(-1);
        }

        // Miniaturas (no expulsamos tan agresivamente porque son pequeñas)
        private void EnforceThumbCacheLimit(int currentPage)
        {
            lock (_lruLock)
            {
                // Por defecto, permitimos hasta el doble del límite de página para thumbs
                int limit = Math.Max(60, _pageCacheLimit * 2);
                if (_thumbCache.Count <= limit) return;
                var ordered = _thumbCache.OrderBy(k => k.Value.ts)
                                          .Where(k => Math.Abs(k.Key - currentPage) > 4)
                                          .Take(_thumbCache.Count - limit)
                                          .Select(k => k.Key)
                                          .ToList();
                foreach (var key in ordered)
                {
                    _thumbCache.TryRemove(key, out _);
                }
            }
        }

        public async Task LoadComicAsync(string filePath = null)
        {
            if (filePath != null)
            {
                _filePath = filePath;
            }
            // Renew cancellation token for this comic load
            try { _internalCts?.Cancel(); } catch { }
            try { _internalCts?.Dispose(); } catch { }
            _internalCts = new CancellationTokenSource();

            _pages.Clear();
            _pageCache.Clear();
            _thumbCache.Clear();
            _archiveKind = ArchiveKind.None;
#if SUPPORT_PDF
            if (_pdfDocument != null) { try { _pdfDocument.Dispose(); } catch (Exception ex) { Logger.LogException("Error disposing PDF document", ex); } _pdfDocument = null; }
#endif
#if SUPPORT_DJVU
            if (_djvuDocument != null) { try { _djvuDocument.Dispose(); } catch (Exception ex) { Logger.LogException("Error disposing DJVU document", ex); } _djvuDocument = null; }
#endif

            try
            {
                using (ComicReader.ContinuousReader.PerformanceLogger.Measure("LoadComicAsync"))
                {
                if (string.IsNullOrEmpty(_filePath))
                {
                    throw new ArgumentException("No se ha especificado un archivo para cargar");
                }
                
                if (!File.Exists(_filePath) && !Directory.Exists(_filePath)) // Puede ser una carpeta
                {
                    throw new FileNotFoundException($"El archivo o carpeta no se encontró: {_filePath}");
                }

                string ext = Path.GetExtension(_filePath).ToLowerInvariant();

                if (Directory.Exists(_filePath))
                {
                    await LoadFolderAsync(_filePath);
                }
                else
                {
                    switch (ext)
                    {
                        case ".cbz":
                            _archiveKind = ArchiveKind.Zip; // esperado
                            await LoadCBZAsync();
                            break;
                        case ".cbr":
                            // Detectar por firma, ya que muchos CBR están renombrados desde ZIP/7Z
                            _archiveKind = DetectArchiveKindFromFile(_filePath, ext);
                            await LoadArchiveByKindAsync(_archiveKind == ArchiveKind.None ? ArchiveKind.Rar : _archiveKind);
                            break;
                        case ".cbt":
                            _archiveKind = ArchiveKind.Tar;
                            await LoadCBTAsync();
                            break;
                        case ".cb7":
                            _archiveKind = ArchiveKind.SevenZip;
                            await LoadCB7Async();
                            break;
                        case ".pdf":
                            await LoadPDFAsync();
                            break;
                        case ".epub":
                            await LoadEPUBAsync();
                            break;
                        case ".djvu":
                            await LoadDJVUAsync();
                            break;
                        // Añadir soporte para imágenes sueltas si se arrastra un solo archivo
                        case ".jpg": case ".jpeg": case ".png": case ".gif": case ".bmp":
                        case ".webp": // Requiere librería externa
                        case ".heic": // Requiere librería externa
                            _pages.Add(new Models.ComicPage(1, _filePath, CreatePlaceholderImage("IMG", 200, 200)));
                            break;
                        default:
                            throw new NotSupportedException($"Formato de archivo no soportado: {ext}");
                    }
                }

                }
                if (_pages.Count == 0)
                {
                    throw new InvalidDataException("El archivo de cómic no contiene páginas de imagen válidas o está vacío.");
                }

                Logger.Log($"Successfully loaded comic structure for: {_filePath} with {_pages.Count} pages.");
                // Intentar detectar rendimiento de almacenamiento y ajustar prefetch
                try { DetectAndAdjustPrefetch(); } catch { }
            }
            catch (InvalidDataException ex)
            {
                Logger.LogException($"Failed to load comic structure for: {_filePath} - Invalid or Corrupt Data.", ex);
                throw new Exception("El archivo de cómic está corrupto o tiene un formato inválido.", ex);
            }
            catch (EndOfStreamException ex)
            {
                Logger.LogException($"Failed to load comic structure for: {_filePath} - Unexpected end of stream.", ex);
                throw new Exception("El archivo de cómic está incompleto o corrupto.", ex);
            }
            catch (Exception ex)
            {
                Logger.LogException($"Failed to load comic structure for: {_filePath}", ex);
                throw;
            }
        }

        // --- Métodos de Carga por Formato ---

        private async Task LoadCBZAsync()
        {
            await Task.Run(() =>
            {
                _archiveKind = ArchiveKind.Zip;
                _archiveSemaphore.Wait();
                try
                {
                    using (var archive = ZipFile.OpenRead(_filePath))
                    {
                        var imageEntries = archive.Entries
                            .Where(e => !string.IsNullOrEmpty(e.FullName) && !e.FullName.EndsWith("/") && IsSupportedImageExtension(Path.GetExtension(e.FullName)))
                            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                            .Select(e => e.FullName)
                            .ToList();
                        for (int i = 0; i < imageEntries.Count; i++)
                        {
                            _pages.Add(new Models.ComicPage(i + 1, imageEntries[i], null));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException($"Error reading CBZ archive: {_filePath}", ex);
                    throw;
                }
                finally
                {
                    _archiveSemaphore.Release();
                }
            }, _internalCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
        }

        private async Task LoadCBRAsync()
        {
            await Task.Run(() =>
            {
                _archiveKind = ArchiveKind.Rar;
                _archiveSemaphore.Wait();
                try
                {
                    using (var archive = RarArchive.Open(_filePath))
                    {
                        var imageEntries = archive.Entries
                            .Where(e => !e.IsDirectory && IsSupportedImageExtension(Path.GetExtension(e.Key)))
                            .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                            .Select(e => e.Key)
                            .ToList();
                        for (int i = 0; i < imageEntries.Count; i++)
                        {
                            _pages.Add(new Models.ComicPage(i + 1, imageEntries[i], null));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException($"Error reading CBR archive: {_filePath}", ex);
                    throw;
                }
                finally
                {
                    _archiveSemaphore.Release();
                }
            }, _internalCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
        }

        private async Task LoadCBTAsync()
        {
            await Task.Run(() =>
            {
                _archiveKind = ArchiveKind.Tar;
                _archiveSemaphore.Wait();
                try
                {
                    using (var archive = TarArchive.Open(_filePath))
                    {
                        var imageEntries = archive.Entries
                            .Where(e => !e.IsDirectory && IsSupportedImageExtension(Path.GetExtension(e.Key)))
                            .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                            .Select(e => e.Key)
                            .ToList();
                        for (int i = 0; i < imageEntries.Count; i++)
                        {
                            _pages.Add(new Models.ComicPage(i + 1, imageEntries[i], null));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException($"Error reading CBT archive: {_filePath}", ex);
                    throw;
                }
                finally
                {
                    _archiveSemaphore.Release();
                }
            }, _internalCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
        }

        private async Task LoadCB7Async()
        {
            await Task.Run(() =>
            {
                _archiveKind = ArchiveKind.SevenZip;
                _archiveSemaphore.Wait();
                try
                {
                    using (var archive = SevenZipArchive.Open(_filePath))
                    {
                        var imageEntries = archive.Entries
                            .Where(e => !e.IsDirectory && IsSupportedImageExtension(Path.GetExtension(e.Key)))
                            .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                            .Select(e => e.Key)
                            .ToList();
                        for (int i = 0; i < imageEntries.Count; i++)
                        {
                            _pages.Add(new Models.ComicPage(i + 1, imageEntries[i], null));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException($"Error reading CB7 archive: {_filePath}", ex);
                    throw;
                }
                finally
                {
                    _archiveSemaphore.Release();
                }
            }, _internalCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
        }

        private async Task LoadFolderAsync(string folderPath)
        {
            await Task.Run(() =>
            {
                var imageFiles = Directory.GetFiles(folderPath)
                    .Where(f => IsSupportedImageExtension(Path.GetExtension(f)))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (int i = 0; i < imageFiles.Count; i++)
                {
                    _pages.Add(new Models.ComicPage(i + 1, imageFiles[i], null));
                }
            }, _internalCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
        }

        private async Task LoadPDFAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    Logger.Log($"PDF detectado: {_filePath}", LogLevel.Info);
                    if (!File.Exists(_filePath))
                        throw new FileNotFoundException($"El archivo PDF no existe: {_filePath}");
#if SUPPORT_PDF
                    _pdfDocument?.Dispose();
                    // Dimensiones configurables para render PDF (fallback a valores por defecto)
                    int targetWidth = Math.Max(200, SettingsManager.Settings?.PdfRenderWidth ?? 1600);
                    int targetHeight = Math.Max(200, SettingsManager.Settings?.PdfRenderHeight ?? 2200);
                    _pdfDocument = DocLib.Instance.GetDocReader(_filePath, new PageDimensions(targetWidth, targetHeight));
                    int pageCount = _pdfDocument.GetPageCount();
                    for (int i = 0; i < pageCount; i++)
                    {
                        _pages.Add(new Models.ComicPage(i + 1, $"PDF_Page_{i + 1}", null));
                    }
#else
                    // Sin soporte PDF en tiempo de compilación: crear una página informativa
                    _pages.Add(new Models.ComicPage(1, "PDF_Info", CreatePlaceholderImage("Instala soporte PDF para renderizar", 500, 700)));
#endif
                }
                catch (Exception ex)
                {
                    Logger.LogException($"Error al cargar PDF: {_filePath}", ex);
                    _pages.Clear();
                    _pages.Add(new Models.ComicPage(1, "PDF_Error", CreatePlaceholderImage($"Error PDF\n{ex.Message}", 500, 700)));
                }
            }, _internalCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
        }

        private async Task LoadEPUBAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var epubBook = VersOne.Epub.EpubReader.ReadBook(_filePath);
                    var imageFiles = epubBook.Content.Images.Local;
                    int idx = 1;
                    foreach (var imgFile in imageFiles)
                    {
                        var imgBytes = imgFile.Content;
                        using (var ms = new MemoryStream(imgBytes))
                        {
                            _pages.Add(new Models.ComicPage(idx, $"EPUB_Image_{idx}", CreateBitmapImage(ms)));
                        }
                        idx++;
                    }
                    if (idx == 1)
                    {
                        _pages.Add(new Models.ComicPage(1, "EPUB Placeholder", CreatePlaceholderImage("EPUB", 200, 200)));
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException($"Error al cargar EPUB: {_filePath}", ex);
                    _pages.Add(new Models.ComicPage(1, "EPUB Error", CreatePlaceholderImage("EPUB", 200, 200)));
                }
            }, _internalCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
        }

        private async Task LoadDJVUAsync()
        {
            await Task.Run(() =>
            {
                try
                {
#if SUPPORT_DJVU
                    _djvuDocument?.Dispose();
                    _djvuDocument = new DjvuDocument(_filePath);
                    int pageCount = _djvuDocument.Pages.Count;
                    for (int i = 0; i < pageCount; i++)
                    {
                        _pages.Add(new Models.ComicPage(i + 1, $"DJVU_Page_{i + 1}", null));
                    }
#else
                    _pages.Add(new Models.ComicPage(1, "DJVU_Info", CreatePlaceholderImage("Instala soporte DJVU para renderizar", 500, 700)));
#endif
                }
                catch (Exception ex)
                {
                    Logger.LogException($"Error al cargar DJVU: {_filePath}", ex);
                    _pages.Clear();
                    _pages.Add(new Models.ComicPage(1, "DJVU_Error", CreatePlaceholderImage($"Error DJVU\n{ex.Message}", 500, 700)));
                }
            }, _internalCts?.Token ?? CancellationToken.None);
        }

        // --- Métodos Auxiliares de Carga ---

        private void AddImageEntries(IEnumerable<string> entryKeys, Func<string, Stream> streamProvider)
        {
            var sortedEntries = entryKeys
                .Where(e => !string.IsNullOrEmpty(e) && !e.EndsWith("/") && IsSupportedImageExtension(Path.GetExtension(e)))
                .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (int i = 0; i < sortedEntries.Count; i++)
            {
                _pages.Add(new Models.ComicPage(i + 1, sortedEntries[i], CreatePlaceholderImage("IMG", 200, 200)));
            }
        }

        private bool IsSupportedImageExtension(string extension)
        {
            return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) || // Requiere librería
                   extension.Equals(".heic", StringComparison.OrdinalIgnoreCase);   // Requiere librería
        }

        // --- Métodos para Obtener Imágenes ---

    public async Task<BitmapImage> GetPageImageAsync(int pageNumber, int targetWidth = 0)
    {
        if (pageNumber < 0 || pageNumber >= _pages.Count) return CreatePlaceholderImage("Error", 200, 200);
        // If full image cached in memory, return immediately
        if (_pageCache.TryGetValue(pageNumber, out var cached) && cached.img != null)
        {
            _pageCache[pageNumber] = (cached.img, DateTime.UtcNow);
            return cached.img;
        }

        // Try multi-level cache (disk -> memory) before doing any decoding
        if (_multiLevelCache != null)
        {
            try
            {
                var cacheKey = $"full_{_filePath}_{pageNumber}_{targetWidth}";
                var diskImg = await _multiLevelCache.Get(cacheKey).ConfigureAwait(false);
                if (diskImg != null)
                {
                    _pageCache[pageNumber] = (diskImg, DateTime.UtcNow);
                    EnforcePageCacheLimit(pageNumber);
                    return diskImg;
                }
            }
            catch { /* ignore cache errors */ }
        }

        // If we have a thumbnail cached, use it as quick response and enqueue full high-priority load
        if (_thumbCache.TryGetValue(pageNumber, out var tcached) && tcached.img != null)
        {
            var quick = tcached.img;
            _pageCache[pageNumber] = (quick, DateTime.UtcNow);
            EnforcePageCacheLimit(pageNumber);
            // Enqueue prioritized full load; do not await
            _ = StartFullLoadForPageAsync(pageNumber, targetWidth);
            return quick;
        }

        // No cache: generate a quick thumbnail and enqueue full load
        try
        {
            var desired = targetWidth <= 0 ? 600 : targetWidth;
            var quickImg = await GetPageThumbnailAsync(pageNumber, desired, 0);
            if (quickImg != null)
            {
                _pageCache[pageNumber] = (quickImg, DateTime.UtcNow);
                _quickCachedAt[pageNumber] = DateTime.UtcNow;
                EnforcePageCacheLimit(pageNumber);
            }
            // Enqueue prioritized full load
            _ = StartFullLoadForPageAsync(pageNumber, targetWidth);
        }
    catch (Exception ex) { Logger.LogException("GetPageImageAsync quick thumbnail error", ex); }

        if (_pageCache.TryGetValue(pageNumber, out var after) && after.img != null) return after.img;
        var placeholder = CreatePlaceholderImage("Cargando", 400, 600);
        _pageCache[pageNumber] = (placeholder, DateTime.UtcNow);
        EnforcePageCacheLimit(pageNumber);
        return placeholder;
    }

    // Overload con CancellationToken
    public async Task<BitmapImage> GetPageImageAsync(int pageNumber, int targetWidth, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return CreatePlaceholderImage("Cancelado", 200, 200);

        if (pageNumber < 0 || pageNumber >= _pages.Count) return CreatePlaceholderImage("Error", 200, 200);

        // If full image cached, return immediately
        if (_pageCache.TryGetValue(pageNumber, out var cached) && cached.img != null)
        {
            _pageCache[pageNumber] = (cached.img, DateTime.UtcNow);
            return cached.img;
        }

        // Try multi-level cache (disk -> memory) before doing any decoding
        if (_multiLevelCache != null && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var cacheKey = $"full_{_filePath}_{pageNumber}_{targetWidth}";
                var diskImg = await _multiLevelCache.Get(cacheKey).ConfigureAwait(false);
                if (diskImg != null)
                {
                    _pageCache[pageNumber] = (diskImg, DateTime.UtcNow);
                    EnforcePageCacheLimit(pageNumber);
                    return diskImg;
                }
            }
            catch { /* ignore cache errors */ }
        }
        // If we have a thumbnail cached, use it as quick response and enqueue full high-priority load
        if (_thumbCache.TryGetValue(pageNumber, out var tcached) && tcached.img != null)
        {
            var quick = tcached.img;
            _pageCache[pageNumber] = (quick, DateTime.UtcNow);
            EnforcePageCacheLimit(pageNumber);
            // Enqueue prioritized full load; do not await
            _ = StartFullLoadForPageAsync(pageNumber, targetWidth, cancellationToken);
            return quick;
        }

        // No cache: generate a quick thumbnail and enqueue full load
        try
        {
            var desired = targetWidth <= 0 ? 600 : targetWidth;
            var quickImg = await GetPageThumbnailAsync(pageNumber, desired, 0, cancellationToken).ConfigureAwait(false);
            if (quickImg != null)
            {
                _pageCache[pageNumber] = (quickImg, DateTime.UtcNow);
                _quickCachedAt[pageNumber] = DateTime.UtcNow;
                EnforcePageCacheLimit(pageNumber);
            }
            // Enqueue prioritized full load
            _ = StartFullLoadForPageAsync(pageNumber, targetWidth, cancellationToken);
        }
    catch (OperationCanceledException) { return CreatePlaceholderImage("Cancelado", 200, 200); }
    catch (Exception ex) { Logger.LogException("GetPageImageAsync (with token) error", ex); }

        if (_pageCache.TryGetValue(pageNumber, out var after) && after.img != null) return after.img;
        var placeholder = CreatePlaceholderImage("Cargando", 400, 600);
        _pageCache[pageNumber] = (placeholder, DateTime.UtcNow);
        EnforcePageCacheLimit(pageNumber);
        return placeholder;
    }

        // Obtener miniatura de una página (decodificada a tamaño pequeño)
        public async Task<BitmapImage> GetPageThumbnailAsync(int pageNumber, int width = 200, int height = 300)
        {
            if (pageNumber < 0 || pageNumber >= _pages.Count) return CreatePlaceholderImage("Error", width, height);
            if (_thumbCache.TryGetValue(pageNumber, out var cached) && cached.img != null)
            {
                _thumbCache[pageNumber] = (cached.img, DateTime.UtcNow);
                return cached.img;
            }
            // Try multi-level cache for thumbnails
            if (_multiLevelCache != null)
            {
                try
                {
                    var cacheKey = $"thumb_{_filePath}_{pageNumber}_{width}_{height}";
                    var diskThumb = await _multiLevelCache.Get(cacheKey).ConfigureAwait(false);
                    if (diskThumb != null)
                    {
                        _thumbCache[pageNumber] = (diskThumb, DateTime.UtcNow);
                        return diskThumb;
                    }
                }
                catch (Exception ex) { Logger.LogException($"GetPageThumbnailAsync multi-level cache read failed for {_filePath} page {pageNumber}", ex); }
            }
            // Si se pasa height==0, mantenemos la relación de aspecto usando solo width
            var targetWidth = Math.Max(80, width);
            BitmapImage image = null;
            try
            {
                image = await Task.Run(() => LoadThumbnailFromSource(pageNumber, targetWidth, height <= 0 ? 0 : height), _internalCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return CreatePlaceholderImage("Cancelado", width, height); }
            catch (Exception ex) { Logger.LogException($"GetPageThumbnailAsync LoadThumbnailFromSource failed for {_filePath} page {pageNumber}", ex); }
            if (image == null) image = CreatePlaceholderImage("Sin imagen", width, height);
            _thumbCache[pageNumber] = (image, DateTime.UtcNow);
            // Persist thumbnail to multi-level cache (background)
            if (_multiLevelCache != null)
            {
                try
                {
                    var cacheKey = $"thumb_{_filePath}_{pageNumber}_{width}_{height}";
                    _ = _multiLevelCache.Set(cacheKey, image);
                }
                catch (Exception ex) { Logger.LogException($"GetPageThumbnailAsync multi-level cache persist failed for {_filePath} page {pageNumber}", ex); }
            }
            EnforceThumbCacheLimit(pageNumber);
            return image;
        }

        // Overload con CancellationToken para permitir cancelación cooperativa
        public async Task<BitmapImage> GetPageThumbnailAsync(int pageNumber, int width, int height, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return CreatePlaceholderImage("Cancelado", width, height);
            if (pageNumber < 0 || pageNumber >= _pages.Count) return CreatePlaceholderImage("Error", width, height);
            if (_thumbCache.TryGetValue(pageNumber, out var cached) && cached.img != null)
            {
                _thumbCache[pageNumber] = (cached.img, DateTime.UtcNow);
                return cached.img;
            }
            // Try multi-level cache for thumbnails
            if (_multiLevelCache != null && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var cacheKey = $"thumb_{_filePath}_{pageNumber}_{width}_{height}";
                    var diskThumb = await _multiLevelCache.Get(cacheKey).ConfigureAwait(false);
                    if (diskThumb != null)
                    {
                        _thumbCache[pageNumber] = (diskThumb, DateTime.UtcNow);
                        return diskThumb;
                    }
                }
                catch (Exception ex) { Logger.LogException($"GetPageThumbnailAsync (token) multi-level cache read failed for {_filePath} page {pageNumber}", ex); }
            }
            var targetWidth = Math.Max(80, width);
            BitmapImage image = null;
            try
            {
                image = await Task.Run(() => LoadThumbnailFromSource(pageNumber, targetWidth, height <= 0 ? 0 : height), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return CreatePlaceholderImage("Cancelado", width, height); }
            catch (Exception ex) { Logger.LogException($"GetPageThumbnailAsync (token) LoadThumbnailFromSource failed for {_filePath} page {pageNumber}", ex); }
            if (image == null) image = CreatePlaceholderImage("Sin imagen", width, height);
            _thumbCache[pageNumber] = (image, DateTime.UtcNow);
            // Persist thumbnail to multi-level cache (background)
            if (_multiLevelCache != null)
            {
                try
                {
                    var cacheKey = $"thumb_{_filePath}_{pageNumber}_{width}_{height}";
                    _ = _multiLevelCache.Set(cacheKey, image);
                }
                catch (Exception ex) { Logger.LogException($"GetPageThumbnailAsync (token) multi-level cache persist failed for {_filePath} page {pageNumber}", ex); }
            }
            EnforceThumbCacheLimit(pageNumber);
            return image;
        }

    private BitmapImage LoadImageFromSource(int pageNumber, int targetWidth = 0)
        {
            if (pageNumber < 0 || pageNumber >= _pages.Count) return CreatePlaceholderImage("Error", 200, 200);

            string fileName = _pages[pageNumber].FileName;
            BitmapImage img = CreatePlaceholderImage("Sin imagen", 200, 200);
            string ext = Path.GetExtension(_filePath).ToLowerInvariant();
            string pageExt = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;

            if (Directory.Exists(_filePath)) // Es una carpeta de imágenes
            {
                using (ComicReader.ContinuousReader.PerformanceLogger.Measure("LoadImageFromSource_FileRead"))
                using (var stream = File.OpenRead(fileName))
                {
                    img = CreateBitmapImage(stream, pageExt);
                }
            }
            else
            {
                switch (ext)
                {
                    case ".cbz":
                    case ".cbr":
                    case ".cbt":
                    case ".cb7":
                        // Usar el tipo real detectado
                        switch (_archiveKind)
                        {
                            case ArchiveKind.Zip:
                                _archiveSemaphore.Wait();
                                try
                                {
                                    using (ComicReader.ContinuousReader.PerformanceLogger.Measure("LoadImageFromSource_ZipExtract"))
                                    using (var archive = ZipFile.OpenRead(_filePath))
                                    {
                                        var entry = archive.Entries.FirstOrDefault(e => e.FullName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                                        if (entry != null)
                                        {
                                            using (var es = entry.Open())
                                            using (var ms = new MemoryStream())
                                            {
                                                es.CopyTo(ms);
                                                ms.Position = 0;
                                                using (ComicReader.ContinuousReader.PerformanceLogger.Measure("Decode_CreateBitmapImage"))
                                                {
                                                    // Limit concurrent decodes to avoid CPU saturation
                                                    _decodeSemaphore.Wait();
                                                    try
                                                    {
                                                        img = CreateBitmapImage(ms, pageExt, targetWidth);
                                                    }
                                                    finally { try { _decodeSemaphore.Release(); } catch { } }
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogException($"Error extracting image from zip: {_filePath} -> {fileName}", ex);
                                    throw;
                                }
                                finally { _archiveSemaphore.Release(); }
                                break;
                            case ArchiveKind.SevenZip:
                                _archiveSemaphore.Wait();
                                try
                                {
                                    using (var archive = SevenZipArchive.Open(_filePath))
                                    {
                                        var entry = archive.Entries.FirstOrDefault(e => e.Key.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                                        if (entry != null)
                                        {
                                                using (var es = entry.OpenEntryStream())
                                                using (var ms = new MemoryStream())
                                                {
                                                    es.CopyTo(ms);
                                                    ms.Position = 0;
                                                    using (ComicReader.ContinuousReader.PerformanceLogger.Measure("Decode_CreateBitmapImage"))
                                                    {
                                                        _decodeSemaphore.Wait();
                                                        try
                                                        {
                                                            img = CreateBitmapImage(ms, pageExt, targetWidth);
                                                        }
                                                        finally { try { _decodeSemaphore.Release(); } catch { } }
                                                    }
                                                }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogException($"Error extracting image from 7z: {_filePath} -> {fileName}", ex);
                                    throw;
                                }
                                finally { _archiveSemaphore.Release(); }
                                break;
                            default:
                                // fallback conservador: intentar con ReaderFactory secuencialmente
                                using (var fs = File.OpenRead(_filePath))
                                using (var reader = SharpCompress.Readers.ReaderFactory.Open(fs))
                                {
                                    while (reader.MoveToNextEntry())
                                    {
                                        if (!reader.Entry.IsDirectory && reader.Entry.Key.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            using (var ms = new MemoryStream())
                                            {
                                                reader.WriteEntryTo(ms);
                                                ms.Position = 0;
                                                using (ComicReader.ContinuousReader.PerformanceLogger.Measure("Decode_CreateBitmapImage"))
                                                {
                                                    _decodeSemaphore.Wait();
                                                    try
                                                    {
                                                        img = CreateBitmapImage(ms, pageExt, targetWidth);
                                                    }
                                                    finally { try { _decodeSemaphore.Release(); } catch { } }
                                                }
                                            }
                                            break;
                                        }
                                    }
                                }
                                break;
                        }
                        break;
                    case ".pdf":
#if SUPPORT_PDF
                        if (_pdfDocument != null)
                        {
                            lock (_lock)
                            {
                                using (var pageReader = _pdfDocument.GetPageReader(pageNumber))
                                {
                                    int w = pageReader.GetPageWidth();
                                    int h = pageReader.GetPageHeight();
                                    var bytes = pageReader.GetImage();
                                    img = RawBgraToBitmapImage(bytes, w, h);
                                }
                            }
                        }
                        else
#endif
                        {
                            img = CreatePlaceholderImage("PDF", 200, 200);
                        }
                        break;
                    case ".epub":
                        // Deshabilitado: EpubBook no implementa IDisposable y referencias a imageContent eliminadas para evitar errores de compilación.
                        break;
                    case ".djvu":
#if SUPPORT_DJVU
                        if (_djvuDocument != null)
                        {
                            lock (_lock)
                            {
                                int dpi = Math.Max(72, SettingsManager.Settings?.PdfRenderDpi ?? 150);
                                using (var bitmap = _djvuDocument.Pages[pageNumber].RenderImage(dpi, dpi))
                                {
                                    img = BitmapToImageSource(bitmap);
                                }
                            }
                        }
                        else
#endif
                        {
                            img = CreatePlaceholderImage("DJVU", 200, 200);
                        }
                        break;
                    // Para imágenes sueltas (webp, heic, etc.)
                    case ".jpg": case ".jpeg": case ".png": case ".gif": case ".bmp":
                    case ".webp": case ".heic":
                        using (var stream = File.OpenRead(_filePath))
                        {
                            img = CreateBitmapImage(stream, ext);
                        }
                        break;
                }
            }
            return img;
        }

        private BitmapImage LoadThumbnailFromSource(int pageNumber, int width, int height)
        {
            if (pageNumber < 0 || pageNumber >= _pages.Count) return CreatePlaceholderImage("Error", width, height);
            string fileName = _pages[pageNumber].FileName;
            string ext = Path.GetExtension(_filePath).ToLowerInvariant();
            string pageExt = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;

            if (Directory.Exists(_filePath))
            {
                using (var stream = File.OpenRead(fileName))
                {
                    return CreateThumbnailImage(stream, width, height);
                }
            }
            else
            {
                switch (ext)
                {
                    case ".cbz":
                    case ".cbr":
                    case ".cbt":
                    case ".cb7":
                        switch (_archiveKind)
                        {
                            case ArchiveKind.Zip:
                                using (var archive = ZipFile.OpenRead(_filePath))
                                {
                                    var entry = archive.Entries.FirstOrDefault(e => e.FullName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                                    if (entry != null)
                                    {
                                        using (var es = entry.Open())
                                        using (var ms = new MemoryStream())
                                        { es.CopyTo(ms); ms.Position = 0; return CreateThumbnailImage(ms, width, height); }
                                    }
                                }
                                break;
                            case ArchiveKind.Rar:
                                using (var archive = RarArchive.Open(_filePath))
                                {
                                    var entry = archive.Entries.FirstOrDefault(e => e.Key.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                                    if (entry != null)
                                    {
                                        using (var es = entry.OpenEntryStream())
                                        using (var ms = new MemoryStream())
                                        { es.CopyTo(ms); ms.Position = 0; return CreateThumbnailImage(ms, width, height); }
                                    }
                                }
                                break;
                            case ArchiveKind.Tar:
                                using (var archive = TarArchive.Open(_filePath))
                                {
                                    var entry = archive.Entries.FirstOrDefault(e => e.Key.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                                    if (entry != null)
                                    {
                                        using (var es = entry.OpenEntryStream())
                                        using (var ms = new MemoryStream())
                                        { es.CopyTo(ms); ms.Position = 0; return CreateThumbnailImage(ms, width, height); }
                                    }
                                }
                                break;
                            case ArchiveKind.SevenZip:
                                using (var archive = SevenZipArchive.Open(_filePath))
                                {
                                    var entry = archive.Entries.FirstOrDefault(e => e.Key.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                                    if (entry != null)
                                    {
                                        using (var es = entry.OpenEntryStream())
                                        using (var ms = new MemoryStream())
                                        { es.CopyTo(ms); ms.Position = 0; return CreateThumbnailImage(ms, width, height); }
                                    }
                                }
                                break;
                            default:
                                using (var fs = File.OpenRead(_filePath))
                                using (var reader = SharpCompress.Readers.ReaderFactory.Open(fs))
                                {
                                    while (reader.MoveToNextEntry())
                                    {
                                        if (!reader.Entry.IsDirectory && reader.Entry.Key.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            using (var ms = new MemoryStream())
                                            {
                                                reader.WriteEntryTo(ms);
                                                ms.Position = 0; return CreateThumbnailImage(ms, width, height);
                                            }
                                        }
                                    }
                                }
                                break;
                        }
                        break;
                    case ".pdf":
#if SUPPORT_PDF
                        if (_pdfDocument != null)
                        {
                            lock (_lock)
                            {
                                using (var pageReader = _pdfDocument.GetPageReader(pageNumber))
                                {
                                    int w = pageReader.GetPageWidth();
                                    int h = pageReader.GetPageHeight();
                                    var bytes = pageReader.GetImage();
                                    using (var ms = EncodeBmpFromBgra(bytes, w, h))
                                    { ms.Position = 0; return CreateThumbnailImage(ms, width, height); }
                                }
                            }
                        }
                        else
#endif
                        {
                            return CreatePlaceholderImage("PDF", width, height);
                        }
                    case ".djvu":
#if SUPPORT_DJVU
                        if (_djvuDocument != null)
                        {
                            lock (_lock)
                            {
                                int dpi = Math.Max(72, SettingsManager.Settings?.PdfRenderDpi ?? 150);
                                using (var bitmap = _djvuDocument.Pages[pageNumber].RenderImage(dpi, dpi))
                                using (var ms = new MemoryStream())
                                {
                                    bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                    ms.Position = 0; return CreateThumbnailImage(ms, width, height);
                                }
                            }
                        }
                        else
#endif
                        {
                            return CreatePlaceholderImage("DJVU", width, height);
                        }
                    case ".jpg": case ".jpeg": case ".png": case ".gif": case ".bmp":
                    case ".webp": case ".heic":
                        using (var stream = File.OpenRead(_filePath))
                        { return CreateThumbnailImage(stream, width, height); }
                }
            }
            return CreatePlaceholderImage("Sin imagen", width, height);
        }

        // Ruta optimizada para formatos comunes usando extensión; fallback a ImageSharp si no es compatible
        private BitmapImage CreateBitmapImage(Stream stream, string extension, int targetWidth = 0)
        {
            try
            {
                if (extension == ".jpg" || extension == ".jpeg" || extension == ".png" || extension == ".gif" || extension == ".bmp")
                {
                    // Decodificación con downscale preventivo para evitar cargar imágenes gigantes a resolución completa.
                    // Usamos el ancho objetivo desde Settings (PdfRenderWidth como aproximación) o 2000px por defecto.
                    int maxTarget = SettingsManager.Settings?.PdfRenderWidth ?? MaxDecodeWidth;
                    // Si se pasó un targetWidth válido, usarlo (permitir que la UI pida un tamaño más ajustado)
                    if (targetWidth <= 0) targetWidth = Math.Max(600, Math.Min(maxTarget, MaxDecodeWidth));
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.DecodePixelWidth = targetWidth; // WPF mantendrá aspect ratio
                    img.StreamSource = stream;
                    img.EndInit();
                    img.Freeze();
                    return img;
                }

                // Fallback a ImageSharp para formatos no compatibles nativamente (webp/heic/etc.)
                stream.Position = 0;
                // Ejecutar la carga/resize/encode en un Task separado y con timeout para evitar bloqueos extremos
                try
                {
                    // Si el token interno fue cancelado antes de iniciar, evitar comenzar trabajo costoso
                    if (_internalCts != null && _internalCts.IsCancellationRequested)
                        return CreatePlaceholderImage("Cancelado", 200, 200);

                    var bmpTask = Task.Run(() =>
                    {
                        var image = SixLabors.ImageSharp.Image.Load(stream);
                        int localMaxTarget = SettingsManager.Settings?.PdfRenderWidth ?? MaxDecodeWidth;
                        int localTargetWidth = Math.Max(600, Math.Min(localMaxTarget, MaxDecodeWidth));
                        if (image.Width > localTargetWidth)
                        {
                            image.Mutate(ctx => ctx.Resize(new SixLabors.ImageSharp.Processing.ResizeOptions
                            {
                                Size = new SixLabors.ImageSharp.Size(localTargetWidth, 0),
                                Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max,
                                Sampler = KnownResamplers.Lanczos3
                            }));
                        }
                        using (var ms = new MemoryStream())
                        {
                            // Usar JPEG para codificación más rápida y menor tamaño en disco; calidad razonable
                            var jpegEncoder = new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 85 };
                            image.Save(ms, jpegEncoder);
                            ms.Position = 0;
                            var img = new BitmapImage();
                            img.BeginInit();
                            img.StreamSource = ms;
                            img.CacheOption = BitmapCacheOption.OnLoad;
                            img.EndInit();
                            img.Freeze();
                            return img;
                        }
                    }, _internalCts?.Token ?? CancellationToken.None);

                    // Esperar con timeout; si supera el límite devolvemos placeholder para no bloquear más
                    if (!bmpTask.Wait(TimeSpan.FromSeconds(8)))
                    {
                        // Cancel is not supported inside ImageSharp load, so just return a placeholder
                        return CreatePlaceholderImage("Sin imagen (timeout)", 200, 200);
                    }
                    return bmpTask.Result;
                }
                catch (Exception ex)
                {
                    // Si falla ImageSharp, intentar fallback a BitmapImage básico
                    try { Logger.LogException("CreateBitmapImage: ImageSharp processing failed, falling back to BitmapImage.", ex); } catch { }
                    var img = new BitmapImage();
                    img.BeginInit();
                    try { stream.Position = 0; } catch { }
                    img.StreamSource = stream;
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.DecodePixelWidth = Math.Max(600, SettingsManager.Settings?.PdfRenderWidth ?? MaxDecodeWidth);
                    img.EndInit();
                    img.Freeze();
                    return img;
                }
            }
            catch (Exception ex)
            {
                // Fallback a BitmapImage estándar si ImageSharp falla
                try { Logger.LogException("CreateBitmapImage failed", ex); } catch { }
                var img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = stream;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.DecodePixelWidth = Math.Max(600, SettingsManager.Settings?.PdfRenderWidth ?? MaxDecodeWidth);
                img.EndInit();
                img.Freeze();
                return img;
            }
        }

        // Mantener compatibilidad con llamadas existentes
        private BitmapImage CreateBitmapImage(Stream stream)
        {
            return CreateBitmapImage(stream, string.Empty, 0);
        }

        private BitmapImage BitmapToImageSource(Bitmap bitmap)
        {
            using (var ms = new MemoryStream())
            {
                // Guardar como BMP (sin compresión) para reducir tiempo de CPU en la conversión
                #pragma warning disable CA1416 // System.Drawing API used for internal bitmap roundtrip (Windows-only)
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                #pragma warning restore CA1416
                ms.Position = 0;
                return CreateBitmapImage(ms);
            }
        }

        // Conversión para Docnet.Core (buffer BGRA)
        private BitmapImage RawBgraToBitmapImage(byte[] bgraBytes, int width, int height)
        {
            var dpiX = 96d;
            var dpiY = 96d;
            var pixelFormat = System.Windows.Media.PixelFormats.Bgra32;
            var stride = (width * pixelFormat.BitsPerPixel + 7) / 8;
            var bmp = BitmapSource.Create(width, height, dpiX, dpiY, pixelFormat, null, bgraBytes, stride);
            // Usar BMP encoder (sin compresión) para minimizar overhead de CPU vs PNG
            var encoder = new BmpBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                ms.Position = 0;
                return CreateBitmapImage(ms);
            }
        }

        // Devuelve un MemoryStream con PNG a partir de BGRA (útil para miniaturas)
        private MemoryStream EncodeBmpFromBgra(byte[] bgraBytes, int width, int height)
        {
            var dpiX = 96d;
            var dpiY = 96d;
            var pixelFormat = System.Windows.Media.PixelFormats.Bgra32;
            var stride = (width * pixelFormat.BitsPerPixel + 7) / 8;
            var bmp = BitmapSource.Create(width, height, dpiX, dpiY, pixelFormat, null, bgraBytes, stride);
            var encoder = new BmpBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            var ms = new MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;
            return ms;
        }

        private BitmapImage CreatePlaceholderImage(string text, int width, int height)
        {
            try
            {
                if (width <= 0) width = 200;
                if (height <= 0) height = 200;

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    // Background
                    dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, width, height));

                    // Formatted text (WPF)
                    var typeface = new Typeface("Segoe UI");
                    double fontSize = Math.Max(12, Math.Min(36, Math.Min(width, height) / 10.0));
                    var ft = new FormattedText(
                        text ?? string.Empty,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        System.Windows.Media.Brushes.Gray,
                        1.0);

                    // Wrap if too wide
                    ft.MaxTextWidth = Math.Max(20, width - 20);
                    // Center
                    double x = Math.Max(0, (width - ft.Width) / 2);
                    double y = Math.Max(0, (height - ft.Height) / 2);
                    dc.DrawText(ft, new System.Windows.Point(x, y));
                }

                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);

                var encoder = new BmpBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var ms = new MemoryStream())
                {
                    encoder.Save(ms);
                    ms.Position = 0;
                    return CreateBitmapImage(ms);
                }
            }
            catch
            {
                // Fallback sencillo: devolver un BitmapImage vacío usando a memory stream
                var img = new BitmapImage();
                try
                {
                    img.BeginInit();
                    img.StreamSource = new MemoryStream();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.EndInit();
                    img.Freeze();
                }
                catch { }
                return img;
            }
        }

        // --- Cache y Pre-carga ---

        public void PreloadPages(int currentPageNumber)
        {
            try
            {
                // Auto-pin current +/- prefetch window to keep high-priority neighbors from being evicted
                UpdatePinnedWindow(currentPageNumber);
            }
            catch { }
            int window = _prefetchWindow > 0 ? _prefetchWindow : 4;
            // Priorizar páginas cercanas y limitar concurrencia para no saturar el I/O
            var toLoad = new List<int>();
            for (int i = 1; i <= window; i++)
            {
                int prev = currentPageNumber - i;
                int next = currentPageNumber + i;
                if (prev >= 0 && !_pageCache.ContainsKey(prev)) toLoad.Add(prev);
                if (next < _pages.Count && !_pageCache.ContainsKey(next)) toLoad.Add(next);
            }

            if (toLoad.Count == 0) return;

            // Use shared prefetch semaphore to avoid I/O saturation and avoid duplicate loads
            foreach (var idx in toLoad)
            {
                if (_pageCache.ContainsKey(idx)) continue; // already loaded
                // start a background load if not already in-flight
                // Enqueue as low priority work so visible page loads take precedence
                ComicReader.ContinuousReader.PerformanceLogger.Log($"Enqueue prefetch page {idx}");
                _renderer.Enqueue(async () =>
                {
                    using (ComicReader.ContinuousReader.PerformanceLogger.Measure($"Prefetch page {idx}"))
                    {
                    // Deduplicate via _ongoingPageLoads similar to previous logic
                    var task = _ongoingPageLoads.GetOrAdd(idx, k => Task.Run(async () =>
                    {
                        await _prefetchSemaphore.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            // ensure a thumbnail is available as a quick response
                            if (!_thumbCache.ContainsKey(k))
                            {
                                try { await GetPageThumbnailAsync(k, 300, 0, _internalCts.Token).ConfigureAwait(false); }
                                catch (Exception ex) { Logger.LogException($"PrefetchPages: GetPageThumbnailAsync failed for {_filePath} page {k}", ex); }
                            }
                            // Enqueue a full high-priority load which will also persist to multi-level cache
                            try { _ = StartFullLoadForPageAsync(k, 1200, _internalCts.Token); } catch (Exception ex) { Logger.LogException($"PrefetchPages: StartFullLoadForPageAsync failed for {_filePath} page {k}", ex); }

                            try { return _pageCache.TryGetValue(k, out var v) ? v.img : CreatePlaceholderImage("Sin imagen", 200, 200); }
                            catch (Exception ex) { Logger.LogException($"PrefetchPages: fetch page cache failed for {_filePath} page {k}", ex); return CreatePlaceholderImage("Sin imagen", 200, 200); }
                        }
                        catch (Exception ex) { Logger.LogException($"PrefetchPages: background load failed for {_filePath} page {k}", ex); return CreatePlaceholderImage("Sin imagen", 200, 200); }
                        finally { _prefetchSemaphore.Release(); _ongoingPageLoads.TryRemove(k, out Task<BitmapImage> _dummy); }
                    }, _internalCts?.Token ?? CancellationToken.None));
                    try { await task.ConfigureAwait(false); } catch (Exception ex) { Logger.LogException($"PrefetchPages: awaiting task failed for {_filePath} page {idx}", ex); }
                    }
                }, highPriority: false);
            }

        }

        // Pin nearby pages (full + thumbnail keys) using the concrete multi-level cache when available.
        private void UpdatePinnedWindow(int centerPage)
        {
            try
            {
                var cache = _multiLevelCache as ComicReader.Core.Services.MultiLevelImageCache;
                    if (cache == null) return;

                int radius = Math.Max(1, _prefetchWindow);
                var newSet = new HashSet<int>();
                for (int i = centerPage - radius; i <= centerPage + radius; i++)
                {
                    if (i < 0 || i >= _pages.Count) continue;
                    newSet.Add(i);
                }

                lock (_lruLock)
                {
                    // Unpin pages that are no longer in new set
                    foreach (var old in _currentlyPinnedPages.ToList())
                    {
                        if (!newSet.Contains(old))
                        {
                            try
                            {
                                var thumbKey = $"thumb_{_filePath}_{old}_300";
                                var fullKey = $"full_{_filePath}_{old}_1200";
                                cache.Unpin(thumbKey);
                                cache.Unpin(fullKey);
                            }
                            catch (Exception ex) { Logger.LogException($"UpdatePinnedWindow: unpin failed for {_filePath} page {old}", ex); }
                        }
                    }

                    // Pin new pages
                    foreach (var idx in newSet)
                    {
                        if (!_currentlyPinnedPages.Contains(idx))
                        {
                            try
                            {
                                var thumbKey = $"thumb_{_filePath}_{idx}_300";
                                var fullKey = $"full_{_filePath}_{idx}_1200";
                                cache.Pin(thumbKey);
                                cache.Pin(fullKey);
                            }
                            catch (Exception ex) { Logger.LogException($"UpdatePinnedWindow: pin failed for {_filePath} page {idx}", ex); }
                        }
                    }

                    _currentlyPinnedPages.Clear();
                    foreach (var v in newSet) _currentlyPinnedPages.Add(v);
                }
            }
            catch (Exception ex) { Logger.LogException($"UpdatePinnedWindow failed for {_filePath}", ex); }
        }

        public void ClearCaches()
        {
            try
            {
                _pageCache.Clear();
                _thumbCache.Clear();
                _ongoingPageLoads.Clear();
                _quickCachedAt.Clear();
                _lastSwapMs = 0;
                Logger.Log("ComicPageLoader caches cleared.");
            }
            catch { }
        }

    // Start a deduplicated, bounded full-image load for a page (used when thumbnail is shown first)
    private Task<BitmapImage> StartFullLoadForPageAsync(int pageNumber, int targetWidth = 0, CancellationToken cancellationToken = default)
        {
            // Enqueue the full load as high priority so it runs before prefetch jobs
            var tcs = new TaskCompletionSource<BitmapImage>(TaskCreationOptions.RunContinuationsAsynchronously);
            ComicReader.ContinuousReader.PerformanceLogger.Log($"Enqueue full load page {pageNumber}");
            _renderer.Enqueue(async () =>
            {
                using (ComicReader.ContinuousReader.PerformanceLogger.Measure($"FullLoad page {pageNumber}"))
                {
                var task = _ongoingPageLoads.GetOrAdd(pageNumber, k =>
                {
                    // Create a task that performs the full load and is cancelable via linkedCTS
                    return Task.Run(async () =>
                    {
                        CancellationTokenSource linkedCts = null;
                        try
                        {
                            await _prefetchSemaphore.WaitAsync().ConfigureAwait(false);
                            if (cancellationToken.IsCancellationRequested || _internalCts.IsCancellationRequested)
                                return CreatePlaceholderImage("Cancelado", 200, 200);

                            linkedCts = cancellationToken.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _internalCts.Token) : CancellationTokenSource.CreateLinkedTokenSource(_internalCts.Token);
                            var tokenToUse = linkedCts.Token;
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            var bmp = await Task.Run(() => LoadImageFromSource(k, targetWidth), tokenToUse).ConfigureAwait(false);
                            sw.Stop();
                            if (bmp == null) bmp = CreatePlaceholderImage("Sin imagen", 200, 200);
                            _pageCache[k] = (bmp, DateTime.UtcNow);
                            // Persist full decoded image to multi-level cache to avoid future blurry reloads
                            try
                            {
                                if (_multiLevelCache != null)
                                {
                                    var cacheKey = $"full_{_filePath}_{k}_{targetWidth}";
                                    _ = _multiLevelCache.Set(cacheKey, bmp);
                                }
                            }
                            catch { }
                            try { FullImageReady?.Invoke(k, bmp); } catch { }
                            EnforcePageCacheLimit(k);
                            if (_quickCachedAt.TryRemove(k, out var quickTs))
                            {
                                var swapMs = (DateTime.UtcNow - quickTs).TotalMilliseconds;
                                _lastSwapMs = swapMs;
                                _log?.Log($"Swap thumbnail->full for page {k} took {swapMs:F0}ms (decode {sw.ElapsedMilliseconds}ms)", LogLevel.Info);
                            }
                            return bmp;
                        }
                        catch (OperationCanceledException) { return CreatePlaceholderImage("Cancelado", 200, 200); }
                        catch (Exception ex)
                        {
                            try { Logger.LogException($"Error loading full image for page {pageNumber}", ex); } catch { }
                            return CreatePlaceholderImage("Sin imagen", 200, 200);
                        }
                        finally
                        {
                            try { _prefetchSemaphore.Release(); } catch (Exception ex) { Logger.LogException($"StartFullLoadForPageAsync: failed to release prefetch semaphore for page {pageNumber}", ex); }
                            try { _ongoingPageLoads.TryRemove(pageNumber, out _); } catch (Exception ex) { Logger.LogException($"StartFullLoadForPageAsync: failed to remove ongoing load entry for page {pageNumber}", ex); }
                            try { linkedCts?.Dispose(); } catch (Exception ex) { Logger.LogException($"StartFullLoadForPageAsync: failed to dispose linkedCts for page {pageNumber}", ex); }
                        }
                    }, _internalCts?.Token ?? CancellationToken.None);
                });

                try
                {
                    var res = await task.ConfigureAwait(false);
                    tcs.TrySetResult(res);
                }
                catch (OperationCanceledException) { tcs.TrySetCanceled(); }
                catch (Exception ex) { tcs.TrySetException(ex); }
                }
            }, highPriority: true);

            return tcs.Task;
        }

        private void DetectAndAdjustPrefetch()
        {
            try
            {
                // Intento simple: medir latencia de lectura secuencial de 4KB para estimar disco
                var testFile = _filePath ?? Directory.GetFiles(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).FirstOrDefault();
                if (string.IsNullOrEmpty(testFile) || !File.Exists(testFile)) return;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using (var fs = File.OpenRead(testFile))
                {
                    byte[] buffer = new byte[4096];
                    fs.Read(buffer, 0, buffer.Length);
                }
                sw.Stop();
                var latency = sw.Elapsed.TotalMilliseconds;
                // heurística: si lectura < 5ms -> SSD, aumentar concurrencia; si > 30ms -> HDD, reducir
                    // heurística: si lectura < 8ms -> SSD, aumentar ventana de prefetch; si > 30ms -> HDD, reducir
                    if (latency < 8) _prefetchWindow = Math.Min(8, _prefetchWindow + 1);
                    else if (latency > 30) _prefetchWindow = Math.Max(1, _prefetchWindow - 1);
                _log?.Log($"Storage read latency heuristic: {latency:F1}ms", LogLevel.Info);
            }
            catch (Exception ex) { Logger.LogException("DetectAndAdjustPrefetch failed", ex); }
        }

        private void EnforcePageCacheLimit(int currentPage)
        {
            lock (_lruLock)
            {
                int limit = _pageCacheLimit;
                if (_pageCache.Count <= limit) return;
                var ordered = _pageCache.OrderBy(k => k.Value.ts)
                                        .Where(k => Math.Abs(k.Key - currentPage) > 2) // no expulsar inmediatas
                                        .Take(_pageCache.Count - limit)
                                        .Select(k => k.Key)
                                        .ToList();
                foreach (var key in ordered)
                {
                    _pageCache.TryRemove(key, out _);
                }
            }
        }

        // --- Disposición ---

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
#if DEBUG
                _log?.Log("Disposing ComicPageLoader and cancelling internal operations.");
#endif
                try { _internalCts?.Cancel(); } catch { }
                try { _internalCts?.Dispose(); } catch { }

                _pageCache.Clear();
                _thumbCache.Clear();
                _pages.Clear();
                
#if SUPPORT_PDF
                try { _pdfDocument?.Dispose(); } catch {}
#endif
#if SUPPORT_DJVU
                try { _djvuDocument?.Dispose(); } catch {}
#endif
                Logger.Log("ComicPageLoader disposed.");
                // Wait a short time for any ongoing page load tasks to finish to avoid orphaned background work.
                try
                {
                    var ongoing = _ongoingPageLoads?.Values.ToArray();
                    if (ongoing != null && ongoing.Length > 0)
                    {
                        try { Task.WaitAll(ongoing, 3000); } catch { }
                    }
                }
                catch { }
            }
        }

        // --- Miniaturas de Portada ---

        public async Task<BitmapImage> GetCoverThumbnailAsync(int width = 150, int height = 200)
        {
            if (_pages.Count == 0)
            {
                // Mostrar el icono de la app como portada por defecto
                try
                {
                    // 1) Intentar cargar desde recurso empaquetado (Build Action: Resource)
                    var packUri = new Uri("pack://application:,,,/icono.ico", UriKind.Absolute);
                    var sri = System.Windows.Application.GetResourceStream(packUri);
                    if (sri != null && sri.Stream != null)
                    {
                        using (var s = sri.Stream)
                        {
                            return CreateThumbnailImage(s, width, height);
                        }
                    }
                }
                catch { /* intentar con copia en disco */ }

                try
                {
                    // 2) Fallback: archivo copiado junto al ejecutable
                    var appDir = AppDomain.CurrentDomain.BaseDirectory;
                    var iconPath = System.IO.Path.Combine(appDir, "icono.ico");
                    if (File.Exists(iconPath))
                    {
                        using (var fs = File.OpenRead(iconPath))
                        {
                            return CreateThumbnailImage(fs, width, height);
                        }
                    }
                }
                catch { /* fallback abajo */ }

                return CreatePlaceholderImage("Sin portada", width, height);
            }

            BitmapImage coverImage = null;
            try
            {
                coverImage = await Task.Run(() =>
                {
                    string fileName = _pages[0].FileName;
                    BitmapImage img = null;
                    string ext = Path.GetExtension(_filePath).ToLowerInvariant();

                    if (Directory.Exists(_filePath))
                    {
                        // Elegir mejor candidato de portada en carpeta
                        var imageFiles = Directory.GetFiles(_filePath)
                            .Where(f => IsSupportedImageExtension(Path.GetExtension(f)))
                            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        var best = SelectBestCover(imageFiles);
                        var path = string.IsNullOrEmpty(best) ? fileName : best;
                        using (var stream = File.OpenRead(path))
                        {
                            img = CreateThumbnailImage(stream, width, height);
                        }
                    }
                    else
                    {
                        switch (ext)
                        {
                            case ".cbz":
                                using (var archive = ZipFile.OpenRead(_filePath))
                                {
                                    var entries = archive.Entries
                                        .Where(e => !string.IsNullOrEmpty(e.FullName) && !e.FullName.EndsWith("/") && IsSupportedImageExtension(Path.GetExtension(e.FullName)))
                                        .Select(e => e.FullName)
                                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                                        .ToList();
                                    var bestName = SelectBestCover(entries);
                                    var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, bestName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                                               ?? archive.Entries.FirstOrDefault(e => !string.IsNullOrEmpty(e.FullName) && !e.FullName.EndsWith("/") && IsSupportedImageExtension(Path.GetExtension(e.FullName)));
                                    if (entry != null)
                                    {
                                        using (var es = entry.Open())
                                        using (var ms = new MemoryStream())
                                        {
                                            es.CopyTo(ms);
                                            ms.Position = 0;
                                            img = CreateThumbnailImage(ms, width, height);
                                        }
                                    }
                                }
                                break;
                            case ".cbr":
                            case ".cbt":
                            case ".cb7":
                                // Usar tipo real detectado para soportar CBR mal renombrados
                                switch (_archiveKind)
                                {
                                    case ArchiveKind.Rar:
                                        using (var archive = RarArchive.Open(_filePath))
                                        {
                                            var names = archive.Entries
                                                .Where(e => !e.IsDirectory && IsSupportedImageExtension(Path.GetExtension(e.Key)))
                                                .Select(e => e.Key)
                                                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                                                .ToList();
                                            var bestName = SelectBestCover(names);
                                            var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.Key, bestName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                                                       ?? archive.Entries.FirstOrDefault(e => !e.IsDirectory && IsSupportedImageExtension(Path.GetExtension(e.Key)));
                                            if (entry != null)
                                            {
                                                using (var es = entry.OpenEntryStream())
                                                using (var ms = new MemoryStream())
                                                {
                                                    es.CopyTo(ms);
                                                    ms.Position = 0;
                                                    img = CreateThumbnailImage(ms, width, height);
                                                }
                                            }
                                        }
                                        break;
                                    case ArchiveKind.Zip:
                                        using (var archive = ZipFile.OpenRead(_filePath))
                                        {
                                            var entries = archive.Entries
                                                .Where(e => !string.IsNullOrEmpty(e.FullName) && !e.FullName.EndsWith("/") && IsSupportedImageExtension(Path.GetExtension(e.FullName)))
                                                .Select(e => e.FullName)
                                                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                                                .ToList();
                                            var bestName = SelectBestCover(entries);
                                            var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, bestName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                                                       ?? archive.Entries.FirstOrDefault(e => !string.IsNullOrEmpty(e.FullName) && !e.FullName.EndsWith("/") && IsSupportedImageExtension(Path.GetExtension(e.FullName)));
                                            if (entry != null)
                                            {
                                                using (var es = entry.Open())
                                                using (var ms = new MemoryStream())
                                                {
                                                    es.CopyTo(ms);
                                                    ms.Position = 0;
                                                    img = CreateThumbnailImage(ms, width, height);
                                                }
                                            }
                                        }
                                        break;
                                    case ArchiveKind.Tar:
                                        using (var archive = TarArchive.Open(_filePath))
                                        {
                                            var names = archive.Entries
                                                .Where(e => !e.IsDirectory && IsSupportedImageExtension(Path.GetExtension(e.Key)))
                                                .Select(e => e.Key)
                                                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                                                .ToList();
                                            var bestName = SelectBestCover(names);
                                            var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.Key, bestName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                                                       ?? archive.Entries.FirstOrDefault(e => !e.IsDirectory && IsSupportedImageExtension(Path.GetExtension(e.Key)));
                                            if (entry != null)
                                            {
                                                using (var es = entry.OpenEntryStream())
                                                using (var ms = new MemoryStream())
                                                {
                                                    es.CopyTo(ms);
                                                    ms.Position = 0;
                                                    img = CreateThumbnailImage(ms, width, height);
                                                }
                                            }
                                        }
                                        break;
                                    case ArchiveKind.SevenZip:
                                        using (var archive = SevenZipArchive.Open(_filePath))
                                        {
                                            var names = archive.Entries
                                                .Where(e => !e.IsDirectory && IsSupportedImageExtension(Path.GetExtension(e.Key)))
                                                .Select(e => e.Key)
                                                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                                                .ToList();
                                            var bestName = SelectBestCover(names);
                                            var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.Key, bestName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                                                       ?? archive.Entries.FirstOrDefault(e => !e.IsDirectory && IsSupportedImageExtension(Path.GetExtension(e.Key)));
                                            if (entry != null)
                                            {
                                                using (var es = entry.OpenEntryStream())
                                                using (var ms = new MemoryStream())
                                                {
                                                    es.CopyTo(ms);
                                                    ms.Position = 0;
                                                    img = CreateThumbnailImage(ms, width, height);
                                                }
                                            }
                                        }
                                        break;
                                    default:
                                        // Fallback genérico
                                        using (var fs = File.OpenRead(_filePath))
                                        using (var reader = SharpCompress.Readers.ReaderFactory.Open(fs))
                                        {
                                            var all = new List<string>();
                                            while (reader.MoveToNextEntry())
                                            {
                                                if (!reader.Entry.IsDirectory && IsSupportedImageExtension(Path.GetExtension(reader.Entry.Key)))
                                                {
                                                    all.Add(reader.Entry.Key);
                                                }
                                            }
                                            var best = SelectBestCover(all);
                                            if (!string.IsNullOrEmpty(best))
                                            {
                                                // Re-abrir para posicionarnos en la entrada elegida
                                                fs.Position = 0;
                                                using var reader2 = SharpCompress.Readers.ReaderFactory.Open(fs);
                                                while (reader2.MoveToNextEntry())
                                                {
                                                    if (!reader2.Entry.IsDirectory && string.Equals(reader2.Entry.Key, best, StringComparison.OrdinalIgnoreCase))
                                                    {
                                                        using var ms = new MemoryStream();
                                                        reader2.WriteEntryTo(ms);
                                                        ms.Position = 0;
                                                        img = CreateThumbnailImage(ms, width, height);
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                        break;
                                }
                                break;
                            case ".pdf":
                                
#if SUPPORT_PDF
                                if (_pdfDocument != null)
                                {
                                    lock (_lock)
                                    {
                                        using (var pageReader = _pdfDocument.GetPageReader(0))
                                        {
                                            var w = pageReader.GetPageWidth();
                                            var h = pageReader.GetPageHeight();
                                            var bytes = pageReader.GetImage();
                                            img = RawBgraToBitmapImage(bytes, w, h);
                                        }
                                    }
                                }
                                else
#endif
                                {
                                    img = CreatePlaceholderImage("PDF", width, height);
                                }
                                break;
                            case ".epub":
                                // Deshabilitado: uso de EpubBook y imageContent para evitar errores de compilación.
                                break;
                            case ".djvu":
                                
#if SUPPORT_DJVU
                                if (_djvuDocument != null)
                                {
                                    lock (_lock)
                                    {
                                        int dpi = Math.Max(72, SettingsManager.Settings?.PdfRenderDpi ?? 150);
                                        using (var bitmap = _djvuDocument.Pages[0].RenderImage(dpi, dpi))
                                        {
                                            img = BitmapToImageSource(bitmap);
                                        }
                                    }
                                }
                                else
#endif
                                {
                                    img = CreatePlaceholderImage("DJVU", width, height);
                                }
                                break;
                            case ".jpg": case ".jpeg": case ".png": case ".gif": case ".bmp":
                            case ".webp": case ".heic":
                                using (var stream = File.OpenRead(_filePath))
                                {
                                    img = CreateThumbnailImage(stream, width, height);
                                }
                                break;
                        }
                    }
                    return img;
                }, _internalCts?.Token ?? CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.LogException($"Failed to get cover thumbnail for {_filePath}", ex);
            }
            return coverImage;
        }

        private BitmapImage CreateThumbnailImage(Stream stream, int width, int height)
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.StreamSource = stream;
            // Decodificar por ancho preferentemente para mantener aspecto y evitar distorsión.
            img.DecodePixelWidth = Math.Max(80, Math.Min(width, 1600));
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return img;
        }

        private bool IsImageFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;

            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || 
                   ext == ".gif" || ext == ".bmp" || ext == ".webp" || 
                   ext == ".tiff" || ext == ".tif";
        }
    }
}

// Tipos de archivo comprimido soportados por detección de firma
internal enum ArchiveKind { None, Zip, Rar, SevenZip, Tar }

namespace ComicReader.Services
{
    public partial class ComicPageLoader
    {
        // Heurística de selección de portada: prioriza nombres comunes y primeras numeraciones, evita créditos/contraportada
        private static string SelectBestCover(IEnumerable<string> entries)
        {
            if (entries == null) return null;
            string best = null;
            int bestScore = int.MinValue;
            foreach (var e in entries)
            {
                int score = ScoreCoverName(e);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = e;
                }
                else if (score == bestScore && best != null)
                {
                    // Desempate: orden alfabético asc
                    if (string.Compare(e, best, StringComparison.OrdinalIgnoreCase) < 0)
                        best = e;
                }
            }
            return best;
        }

        private static int ScoreCoverName(string path)
        {
            if (string.IsNullOrEmpty(path)) return int.MinValue;
            var name = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            var full = path.Replace('\\', '/').ToLowerInvariant();
            int score = 0;

            // Palabras clave positivas (portada)
            if (name.Contains("cover")) score += 140;
            if (name.Contains("portada")) score += 140;
            if (name.Contains("front")) score += 100;
            if (name.Contains("caratula") || name.Contains("carátula") || name.Contains("cubierta") || name.Contains("tapa")) score += 100;
            if (name.Equals("folder") || name.Contains("folder")) score += 70;
            if (full.Contains("/cover") || full.Contains("/covers") || full.Contains("/portada") || full.Contains("/front")) score += 70;

            // Palabras clave negativas
            if (name.Contains("back") || name.Contains("contraport")) score -= 140;
            if (name.Contains("credit") || name.Contains("crédit")) score -= 120;
            if (name.Contains("indice") || name.Contains("índice") || name.Contains("index") || name.Contains("contenido") || name.Contains("sumario")) score -= 80;
            if (name.Contains("ads") || name.Contains("advert") || name.Contains("thanks") || name.Contains("agradec") || name.Contains("publicidad") || name.Contains("anuncio")) score -= 60;
            if (name.Contains("presenta") || name.Contains("prologo") || name.Contains("prólogo") || name.Contains("intro") || name.Contains("introduc")) score -= 50;
            if (name.Contains("preview") || name.Contains("sketch") || name.Contains("extra") || name.Contains("extras") || name.Contains("bonus")) score -= 60;
            if (full.Contains("/extras/") || full.Contains("/extra/") || full.Contains("/bonus/") || full.Contains("/preview/") || full.Contains("/sketch/")) score -= 60;

            // Preferir numeración baja (000, 001, 01)
            int leading = 0;
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsDigit(name[i])) leading = leading * 10 + (name[i] - '0'); else break;
            }
            // Penalizar explícitamente 000 (suele ser créditos) y favorecer 001
            var onlyDigits = new string(name.TakeWhile(char.IsDigit).ToArray());
            if (onlyDigits == "000") score -= 40;
            if (onlyDigits == "001") score += 35;
            if (leading == 0 && name.Length > 0 && char.IsDigit(name[0])) score += 30; // 000 puede ser portada, pero menor peso
            else if (leading == 1) score += 35;
            else if (leading <= 3) score += 20;
            else if (leading <= 5) score += 10;

            // Bonus leve si el nombre es corto
            if (name.Length <= 8) score += 5;

            // Preferir archivos en la raíz del zip (menos subcarpetas)
            int depth = full.Count(c => c == '/');
            score += Math.Max(0, 3 - depth) * 5; // más puntos si depth 0 o 1

            return score;
        }
        // Detección rápida por firma (cabecera) del tipo real de archivo
        private static ArchiveKind DetectArchiveKindFromFile(string path, string ext)
        {
            try
            {
                using var fs = File.OpenRead(path);
                Span<byte> header = stackalloc byte[8];
                int read = fs.Read(header);
                if (read >= 4)
                {
                    // ZIP: 50 4B 03 04 | 50 4B 05 06 | 50 4B 07 08
                    if (header[0] == 0x50 && header[1] == 0x4B &&
                        ((header[2] == 0x03 && header[3] == 0x04) ||
                         (header[2] == 0x05 && header[3] == 0x06) ||
                         (header[2] == 0x07 && header[3] == 0x08)))
                        return ArchiveKind.Zip;

                    // RAR 4.x: 52 61 72 21 1A 07 00 ; RAR5: 52 61 72 21 1A 07 01 00
                    if (read >= 7 && header[0] == 0x52 && header[1] == 0x61 && header[2] == 0x72 && header[3] == 0x21 && header[4] == 0x1A && header[5] == 0x07 && (header[6] == 0x00 || header[6] == 0x01))
                        return ArchiveKind.Rar;

                    // 7Z: 37 7A BC AF 27 1C
                    if (header[0] == 0x37 && header[1] == 0x7A && header[2] == 0xBC && header[3] == 0xAF && header[4] == 0x27 && header[5] == 0x1C)
                        return ArchiveKind.SevenZip;
                }
            }
            catch { }

            // TAR carece de una firma fuerte, asumimos por extensión
            if (string.Equals(ext, ".cbt", StringComparison.OrdinalIgnoreCase) || string.Equals(ext, ".tar", StringComparison.OrdinalIgnoreCase))
                return ArchiveKind.Tar;

            // Si nada coincide, devolvemos None
            return ArchiveKind.None;
        }

        // Carga genérica en función del tipo real detectado
        private async Task LoadArchiveByKindAsync(ArchiveKind kind)
        {
            switch (kind)
            {
                case ArchiveKind.Zip:
                    await LoadCBZAsync();
                    break;
                case ArchiveKind.Rar:
                    try { await LoadCBRAsync(); }
                    catch (SharpCompress.Common.InvalidFormatException)
                    {
                        // Fallback silencioso: algunos CBR son ZIP/7Z renombrados
                        var detected = DetectArchiveKindFromFile(_filePath, Path.GetExtension(_filePath));
                        if (detected == ArchiveKind.Zip) { await LoadCBZAsync(); return; }
                        if (detected == ArchiveKind.SevenZip) { await LoadCB7Async(); return; }
                        throw;
                    }
                    break;
                case ArchiveKind.SevenZip:
                    await LoadCB7Async();
                    break;
                case ArchiveKind.Tar:
                    await LoadCBTAsync();
                    break;
                default:
                    // Intentar con lector genérico como último recurso
                    await Task.Run(() =>
                    {
                        try
                        {
                            using var fs = File.OpenRead(_filePath);
                            using var reader = SharpCompress.Readers.ReaderFactory.Open(fs);
                            var list = new List<string>();
                            while (reader.MoveToNextEntry())
                            {
                                if (!reader.Entry.IsDirectory && IsSupportedImageExtension(Path.GetExtension(reader.Entry.Key)))
                                    list.Add(reader.Entry.Key);
                            }
                            list = list.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                            for (int i = 0; i < list.Count; i++)
                                _pages.Add(new Models.ComicPage(i + 1, list[i], null));
                        }
                        catch (Exception ex)
                        {
                            Logger.LogException($"Fallback generic reader failed for {_filePath}", ex);
                            throw;
                        }
                    }, _internalCts?.Token ?? CancellationToken.None);
                    break;
            }
        }
    }
}