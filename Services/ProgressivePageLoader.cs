using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using ComicReader.Core.Abstractions;
using ComicReader.Models;

namespace ComicReader.Services
{
    /// <summary>
    /// Loader progresivo que implementa niveles LowRes/HighRes, prefetch predictivo,
    /// caché multinivel (RAM LRU + disk metadata), cancelación y métricas básicas.
    /// Está diseñado para integrarse con la vista continua existente sin cambiar su API pública (IComicPageLoader).
    /// </summary>
    public class ProgressivePageLoader : IComicPageLoader, IDisposable
    {
        // Shared frozen 1x1 transparent placeholder to avoid allocating empty BitmapImage repeatedly
        private static readonly BitmapImage _frozenPlaceholder = CreateFrozenPlaceholder();

        private static BitmapImage CreateFrozenPlaceholder()
        {
            try
            {
                // Create a 1x1 transparent PNG in-memory and load it as frozen BitmapImage
                var encoder = new PngBitmapEncoder();
                var frame = BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[4], 4));
                encoder.Frames.Add(frame);
                using (var ms = new MemoryStream())
                {
                    encoder.Save(ms);
                    ms.Position = 0;
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = ms;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
            }
            catch { return new BitmapImage(); }
        }
        // Inner reusable ComicPageLoader instance to avoid repeated heavy initialization
        private ComicPageLoader _innerLoader;

        // Exponer páginas descubierto por análisis (manifest)
        private List<ComicPage> _pages = new List<ComicPage>();
        public List<ComicPage> Pages => _pages;
        public string FilePath { get; private set; } = string.Empty;
        public string ComicTitle => Path.GetFileNameWithoutExtension(FilePath ?? string.Empty);

        public event Action<int, BitmapImage> FullImageReady;

        // Cachés
        private readonly ConcurrentDictionary<int, (BitmapImage low, BitmapImage high, DateTime ts)> _ramCache = new();
    // metadata cache simple (cold) - persist manifest to disk
    private readonly string _manifestPath;
    // state persistence (position/zoom)
    private readonly string _statePath;

        // Prefetch and worker control
        private readonly SemaphoreSlim _renderSemaphore;
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _inflight = new();
    // Token para operaciones de largo plazo iniciadas por este loader (eager preload, etc.)
    private CancellationTokenSource _cts = new CancellationTokenSource();
        // Track background tasks started by this loader so we can await them on Dispose
        private readonly System.Collections.Concurrent.ConcurrentBag<Task> _backgroundTasks = new System.Collections.Concurrent.ConcurrentBag<Task>();

        // Heurísticas y límites
        private int _preloadAhead = 4;
        private int _preloadBehind = 2;
        private double _lowResScale = 0.25;
        private int _highResDelayMs = 250;
        private int _evictionDistance = 10;
        private long _cacheMemoryLimitBytes = 0; // calculated

        // Metrics
        private long _hits = 0;
        private long _misses = 0;
        private readonly object _lruLock = new object();

        // Simple LRU tracking by last access time
        private readonly ConcurrentDictionary<int, DateTime> _lastAccess = new();

        public ProgressivePageLoader()
        {
            // Set defaults
            _renderSemaphore = new SemaphoreSlim(Math.Max(1, Math.Min(4, Environment.ProcessorCount)));
            _manifestPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PercysLibrary", "manifests");
            try { Directory.CreateDirectory(_manifestPath); } catch { }
            _statePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PercysLibrary", "loaderstate.json");
            // Memory limit: 25% of physical memory as default
            try
            {
                #pragma warning disable CA1416 // Microsoft.VisualBasic.Devices.ComputerInfo is Windows-only
                var pc = new Microsoft.VisualBasic.Devices.ComputerInfo();
                var total = (long)pc.TotalPhysicalMemory;
                _cacheMemoryLimitBytes = Math.Max(50 * 1024 * 1024, total / 4);
                #pragma warning restore CA1416
            }
            catch { _cacheMemoryLimitBytes = 256 * 1024 * 1024; }
        }

        public async Task LoadComicAsync(string filePath = null)
        {
            if (!string.IsNullOrEmpty(filePath)) FilePath = filePath;
            if (string.IsNullOrEmpty(FilePath)) throw new ArgumentException("filePath required");
            // Cancel any previous work and reset per-loader cancellation token so new loads don't race with old tasks
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            _cts = new CancellationTokenSource();

            _pages.Clear();
            _ramCache.Clear();
            _lastAccess.Clear();
            // Cancel and dispose any inflight per-page CTS from previous load
            try
            {
                foreach (var kv in _inflight.Values)
                {
                    try { kv.Cancel(); } catch { }
                    try { kv.Dispose(); } catch { }
                }
            }
            catch { }
            _inflight.Clear();
                // Create a lightweight manifest: page count and file names (delegate to existing ComicPageLoader for discovery)
                var fallback = new ComicPageLoader(FilePath);
                await fallback.LoadComicAsync(FilePath);
                // Reuse the same inner loader for subsequent thumbnail/high-res requests to avoid repeated initialization cost
                _innerLoader = fallback;
            foreach (var p in fallback.Pages)
            {
                _pages.Add(new ComicPage(p.PageIndex, p.FileName, null) { PageNumber = p.PageNumber });
            }
            // persist manifest quick
            try { File.WriteAllText(GetManifestFileName(), string.Join('\n', _pages.Select(p => p.FileName))); } catch { }
            // try to restore previous state for this file (position/zoom)
            try { RestoreState(); } catch { }

            // Si el usuario activó precarga completa en settings, lanzarla en background
            try
            {
                if (SettingsManager.Settings?.EnableEagerPreload == true)
                {
                    // Lanzar en background y trackear la tarea para que podamos esperar en Dispose
                    try
                    {
                        var t = EagerPreloadAllAsync(SettingsManager.Settings?.EagerPreloadConcurrency ?? 3, _cts.Token);
                        _backgroundTasks.Add(t);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private string GetManifestFileName()
        {
            var safe = Path.GetFileName(FilePath).Replace(Path.DirectorySeparatorChar, '_').Replace(':', '_');
            return Path.Combine(_manifestPath, safe + ".manifest");
        }

        public async Task<BitmapImage> GetPageImageAsync(int pageNumber, int targetWidth = 0)
        {
            if (pageNumber < 0 || pageNumber >= _pages.Count) return CreatePlaceholder("Error", 200, 200);

            // Check high-res first
            if (_ramCache.TryGetValue(pageNumber, out var cached))
            {
                _lastAccess[pageNumber] = DateTime.UtcNow;
                Interlocked.Increment(ref _hits);
                return cached.high ?? cached.low;
            }

            Interlocked.Increment(ref _misses);

            // Produce low-res quickly
            var low = await GenerateLowResAsync(pageNumber, targetWidth);
            // store low
            _ramCache[pageNumber] = (low, null, DateTime.UtcNow);
            _lastAccess[pageNumber] = DateTime.UtcNow;

            // schedule high-res after a small delay; allow cancellation if user scrolls away
            // Create a high-res task linked to the loader cancellation token so Dispose can cancel scheduling
            var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            // try to register the per-page CTS; if another inflight exists, dispose our linked and skip scheduling
            if (!_inflight.TryAdd(pageNumber, linked))
            {
                try { linked.Dispose(); } catch { }
            }
            else
            {
                var _highResTask = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(_highResDelayMs, linked.Token).ConfigureAwait(false);
                        if (linked.IsCancellationRequested) return;
                        await _renderSemaphore.WaitAsync(linked.Token).ConfigureAwait(false);
                        try
                        {
                            var high = await GenerateHighResAsync(pageNumber, targetWidth, linked.Token).ConfigureAwait(false);
                            if (high != null)
                            {
                                _ramCache.AddOrUpdate(pageNumber, (low, high, DateTime.UtcNow), (k, v) => (v.low ?? low, high, DateTime.UtcNow));
                                FullImageReady?.Invoke(pageNumber, high);
                                _lastAccess[pageNumber] = DateTime.UtcNow;
                                EnforceMemoryLimit();
                            }
                        }
                        finally { _renderSemaphore.Release(); }
                    }
                    catch (OperationCanceledException) { }
                    catch { }
                    finally
                    {
                        if (_inflight.TryRemove(pageNumber, out var removed))
                        {
                            try { removed.Dispose(); } catch { }
                        }
                    }
                }, linked.Token);
                try { _backgroundTasks.Add(_highResTask); } catch { }
            }

            return low;
        }

        private BitmapImage CreatePlaceholder(string text, int w, int h)
        {
            // Return a shared frozen 1x1 placeholder to avoid expensive allocations on UI thread.
            return _frozenPlaceholder;
        }

        private Task<BitmapImage> GenerateLowResAsync(int pageNumber, int targetWidth, CancellationToken cancellation = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    // Reuse inner loader if available to avoid repeated heavy initialization
                    if (_innerLoader == null)
                    {
                        var tmpInit = new ComicPageLoader(FilePath);
                        await tmpInit.LoadComicAsync(FilePath).ConfigureAwait(false);
                        _innerLoader = tmpInit;
                    }
                    var desired = targetWidth <= 0 ? 800 : targetWidth;
                    var thumb = await _innerLoader.GetPageThumbnailAsync(pageNumber, (int)(desired * _lowResScale), 0, cancellation).ConfigureAwait(false);
                    return thumb;
                }
                catch { return CreatePlaceholder("Low", 200, 300); }
            }, cancellation);
        }

        private Task<BitmapImage> GenerateHighResAsync(int pageNumber, int targetWidth = 0, CancellationToken cancellation = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    if (_innerLoader == null)
                    {
                        var tmpInit = new ComicPageLoader(FilePath);
                        await tmpInit.LoadComicAsync(FilePath).ConfigureAwait(false);
                        _innerLoader = tmpInit;
                    }
                    var high = await _innerLoader.GetPageImageAsync(pageNumber, targetWidth, cancellation).ConfigureAwait(false);
                    return high;
                }
                catch { return null; }
            }, cancellation);
        }

        public void PreloadPages(int currentPageNumber)
        {
            // Predict direction by comparing last access times
            // Simple strategy: preload ahead and behind with different priorities
            var toPreload = new List<int>();
            for (int i = 1; i <= _preloadAhead; i++)
            {
                var idx = currentPageNumber + i;
                if (idx >= 0 && idx < _pages.Count) toPreload.Add(idx);
            }
            for (int i = 1; i <= _preloadBehind; i++)
            {
                var idx = currentPageNumber - i;
                if (idx >= 0 && idx < _pages.Count) toPreload.Add(idx);
            }
            foreach (var idx in toPreload)
            {
                // if not cached, trigger low-res generation and schedule high-res
                if (!_ramCache.ContainsKey(idx) && !_inflight.ContainsKey(idx))
                {
                    // Try disk cache warm hit before generating
                    var keyLow = CacheKey(FilePath, idx, "low");
                    if (DiskImageCache.TryGet(keyLow, out var pathLow))
                    {
                        try
                        {
                            var img = new BitmapImage();
                            using (var fs = File.OpenRead(pathLow)) { img.BeginInit(); img.StreamSource = fs; img.CacheOption = BitmapCacheOption.OnLoad; img.EndInit(); img.Freeze(); }
                            _ramCache[idx] = (img, null, DateTime.UtcNow);
                            _lastAccess[idx] = DateTime.UtcNow;
                            continue;
                        }
                        catch { }
                    }
                    try { var t = GetPageImageAsync(idx, 800); _backgroundTasks.Add(t); } catch { }
                }
            }
        }

        private string CacheKey(string file, int page, string quality) => $"{file}|{page}|{quality}";

        /// <summary>
        /// Precarga todas las páginas en background respetando un límite de concurrencia.
        /// Guarda versiones low/high en DiskImageCache para próximos arranques.
        /// </summary>
        public async Task EagerPreloadAllAsync(int concurrency = 3, CancellationToken cancellation = default, IProgress<(int done, int total)> progress = null)
        {
            await EagerPreloadAllAsync(0, concurrency, cancellation, progress).ConfigureAwait(false);
        }

        // Sobrecarga: permite especificar el índice de página actual para priorizar páginas cercanas primero
        public async Task EagerPreloadAllAsync(int startIndex, int concurrency, CancellationToken cancellation, IProgress<(int done, int total)> progress)
        {
            try
            {
                if (string.IsNullOrEmpty(FilePath) || _pages == null || _pages.Count == 0) return;
                int cap = Math.Max(1, Math.Min(16, concurrency));
                using var sem = new SemaphoreSlim(cap);
                var tasks = new List<Task>();
                var fallbackLoader = new ComicPageLoader(FilePath);
                await fallbackLoader.LoadComicAsync(FilePath).ConfigureAwait(false);
                // Priorizar páginas cercanas a startIndex para mejor experiencia al abrir en una página concreta
                var order = Enumerable.Range(0, _pages.Count)
                    .OrderBy(i => Math.Abs(i - startIndex))
                    .ThenBy(i => i)
                    .ToList();
                int total = order.Count;
                int done = 0;
                // report initial state
                try { progress?.Report((done, total)); } catch { }
                for (int i = 0; i < order.Count; i++)
                {
                    if (cancellation.IsCancellationRequested) break;
                    await sem.WaitAsync(cancellation).ConfigureAwait(false);
                    var idx = order[i];
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            // 1) try disk cache low: if not present, generate thumbnail and save
                            var keyLow = CacheKey(FilePath, idx, "low");
                            if (!DiskImageCache.TryGet(keyLow, out var pathLow))
                            {
                                try
                                {
                                    var thumb = await fallbackLoader.GetPageThumbnailAsync(idx, 600, 0).ConfigureAwait(false);
                                    if (thumb != null)
                                    {
                                        var encoder = new PngBitmapEncoder();
                                        encoder.Frames.Add(BitmapFrame.Create(thumb));
                                        using (var ms = new MemoryStream())
                                        {
                                            encoder.Save(ms);
                                            await DiskImageCache.SaveAsync(keyLow, ms.ToArray()).ConfigureAwait(false);
                                        }
                                    }
                                }
                                catch { }
                            }

                            // 2) try disk cache high: if not present, generate full image and save
                            var keyHigh = CacheKey(FilePath, idx, "high");
                            if (!DiskImageCache.TryGet(keyHigh, out var pathHigh))
                            {
                                try
                                {
                                    var img = await fallbackLoader.GetPageImageAsync(idx, 1200).ConfigureAwait(false);
                                    if (img != null)
                                    {
                                        var encoder2 = new PngBitmapEncoder();
                                        encoder2.Frames.Add(BitmapFrame.Create(img));
                                        using (var ms = new MemoryStream())
                                        {
                                            encoder2.Save(ms);
                                            await DiskImageCache.SaveAsync(keyHigh, ms.ToArray()).ConfigureAwait(false);
                                        }
                                    }
                                }
                                catch { }
                            }

                            // 3) If configured, insert low-res into memory cache respecting limits
                            try
                            {
                                if (SettingsManager.Settings?.EnableEagerPreloadInMemory == true)
                                {
                                    if (!_ramCache.ContainsKey(idx))
                                    {
                                        var lowImg = await fallbackLoader.GetPageThumbnailAsync(idx, 800, 0).ConfigureAwait(false);
                                        if (lowImg != null)
                                        {
                                            _ramCache[idx] = (lowImg, null, DateTime.UtcNow);
                                            _lastAccess[idx] = DateTime.UtcNow;
                                            try { EnforceMemoryLimit(); } catch { }
                                            int memLimit = SettingsManager.Settings?.EagerPreloadMemoryLimitPages ?? 20;
                                            if (_ramCache.Count > memLimit)
                                            {
                                                var keysToRemove = _ramCache.Keys.OrderBy(k => _lastAccess.ContainsKey(k) ? _lastAccess[k] : DateTime.UtcNow)
                                                    .Take(_ramCache.Count - memLimit)
                                                    .ToList();
                                                foreach (var k in keysToRemove) _ramCache.TryRemove(k, out _);
                                            }
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                        finally
                        {
                            try { sem.Release(); } catch { }
                            try { var current = Interlocked.Increment(ref done); progress?.Report((current, total)); } catch { }
                        }
                    }, cancellation));
                }
                // esperar a que terminen todas (pero respetar cancellation)
                try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { }
                // Cleanup disk cache to limit storage used by eager preload (user setting)
                try
                {
                    var maxBytes = SettingsManager.Settings?.EagerPreloadDiskMaxBytes ?? 0;
                    if (maxBytes > 0)
                    {
                        DiskImageCache.CleanupOldByBytes(maxBytes);
                    }
                    else
                    {
                        DiskImageCache.CleanupOld(SettingsManager.Settings?.EagerPreloadDiskMaxFiles ?? 500);
                    }
                }
                catch { }
            }
            catch { }
        }

        private void PersistState(int lastPage = -1, double zoom = 1.0)
        {
            try
            {
                var state = new { File = FilePath, LastPage = lastPage, Zoom = zoom, Timestamp = DateTime.UtcNow };
                File.WriteAllText(_statePath, System.Text.Json.JsonSerializer.Serialize(state));
            }
            catch { }
        }

        private void RestoreState()
        {
            try
            {
                if (!File.Exists(_statePath)) return;
                var raw = File.ReadAllText(_statePath);
                var doc = System.Text.Json.JsonDocument.Parse(raw);
                if (!doc.RootElement.TryGetProperty("File", out var f)) return;
                if (f.GetString() != FilePath) return;
                if (doc.RootElement.TryGetProperty("LastPage", out var lp))
                {
                    var last = lp.GetInt32();
                    // we don't directly reposition here, but the VM puede leer este archivo si se desea
                }
            }
            catch { }
        }

        public void ReleaseDistantPages(int currentIndex)
        {
            try
            {
                var keys = _ramCache.Keys.ToList();
                foreach (var k in keys)
                {
                    if (Math.Abs(k - currentIndex) > _evictionDistance)
                    {
                        _ramCache.TryRemove(k, out _);
                        _lastAccess.TryRemove(k, out _);
                    }
                }
            }
            catch { }
        }

        public (long memoryUsed, long hits, long misses) GetCacheStats()
        {
            long mem = EstimateMemoryUsage();
            return (mem, _hits, _misses);
        }

        private long EstimateMemoryUsage()
        {
            // very rough: assume each bitmap ~ 3 * pixelCount bytes; use PixelWidth/Height if available
            long sum = 0;
            foreach (var kv in _ramCache)
            {
                var low = kv.Value.low;
                var high = kv.Value.high;
                if (low != null) sum += EstimateBitmap(low);
                if (high != null) sum += EstimateBitmap(high);
            }
            return sum;
        }

        private long EstimateBitmap(BitmapImage b)
        {
            try { return (long)(b.PixelWidth * b.PixelHeight * 4); } catch { return 1024 * 1024; }
        }

        private void EnforceMemoryLimit()
        {
            try
            {
                var used = EstimateMemoryUsage();
                if (used <= _cacheMemoryLimitBytes) return;
                // evict LRU entries far from visible (we don't know visible here), so evict globally oldest
                var ordered = _lastAccess.OrderBy(k => k.Value).Select(k => k.Key).ToList();
                foreach (var k in ordered)
                {
                    if (used <= _cacheMemoryLimitBytes * 0.9) break;
                    if (_ramCache.TryRemove(k, out var v))
                    {
                        used -= (EstimateBitmap(v.low) + (v.high != null ? EstimateBitmap(v.high) : 0));
                        _lastAccess.TryRemove(k, out _);
                    }
                }
            }
            catch { }
        }

        // API compatible
        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            foreach (var c in _inflight.Values) try { c.Cancel(); } catch { }
            _inflight.Clear();
            _ramCache.Clear();
            try { _innerLoader?.Dispose(); } catch { }
            // Wait for background tasks to finish but don't block indefinitely. Give a slightly longer grace period.
            try
            {
                var tasks = _backgroundTasks.ToArray();
                if (tasks.Length > 0)
                {
                    Task.WaitAll(tasks, 5000);
                }
            }
            catch { }
        }
    }
}
