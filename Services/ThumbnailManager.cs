using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace ComicReader.Services
{
    /// <summary>
    /// Simple background thumbnail generator with limited concurrency and disk cache.
    /// Designed to be safe to call from ViewModels and tests.
    /// </summary>
    public class ThumbnailManager : IDisposable
    {
        private SemaphoreSlim _semaphore;
        private readonly ConcurrentDictionary<string, Task> _tasks = new ConcurrentDictionary<string, Task>(StringComparer.OrdinalIgnoreCase);
    private readonly string _cacheDir;
    private int _maxCacheFiles;
    private long _maxCacheBytes;
    private readonly object _sync = new object();
    private bool _disposed;

        public ThumbnailManager(int maxConcurrency = 2, int maxCacheFiles = 500, long maxCacheBytes = 200 * 1024 * 1024, string cacheDirectory = null)
        {
            _semaphore = new SemaphoreSlim(maxConcurrency);
            if (!string.IsNullOrWhiteSpace(cacheDirectory))
            {
                _cacheDir = cacheDirectory;
            }
            else
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                _cacheDir = Path.Combine(appData, "PercysLibrary", "Thumbs");
            }
            Directory.CreateDirectory(_cacheDir);
            // Allow test-friendly small values; minimum 1 file and minimum 1KB
            _maxCacheFiles = Math.Max(1, maxCacheFiles);
            _maxCacheBytes = Math.Max(1024, maxCacheBytes);
        }

        /// <summary>
        /// Reconfigure runtime limits (concurrency and cache limits). Safe to call concurrently.
        /// </summary>
        public void Reconfigure(int maxConcurrency, int maxCacheFiles, long maxCacheBytes)
        {
            try
            {
                lock (_sync)
                {
                    if (maxConcurrency > 0 && (_semaphore == null || _semaphore.CurrentCount != maxConcurrency))
                    {
                        try { _semaphore?.Dispose(); } catch (Exception ex) { Logger.LogException("ThumbnailManager.Reconfigure - dispose semaphore failed", ex); }
                        _semaphore = new SemaphoreSlim(Math.Max(1, maxConcurrency));
                    }
                    _maxCacheFiles = Math.Max(1, maxCacheFiles);
                    _maxCacheBytes = Math.Max(1024, maxCacheBytes);
                }
                // perform a maintenance pass to enforce new limits
                try { EnforceCacheLimit(); } catch (Exception ex) { Logger.LogException("ThumbnailManager.Reconfigure - EnforceCacheLimit failed", ex); }
            }
            catch (Exception ex) { Logger.LogException("ThumbnailManager.Reconfigure - unexpected error", ex); }
        }

        public string TryGetCached(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return null;
                var cachePath = ComputePath(filePath);
                return File.Exists(cachePath) ? cachePath : null;
            }
            catch (Exception ex) { Logger.LogException("ThumbnailManager.TryGetCached failed", ex); return null; }
        }

        public string ComputePath(string filePath)
        {
            using (var sha1 = SHA1.Create())
            {
                var key = "v2|" + filePath;
                var hash = BitConverter.ToString(sha1.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", string.Empty);
                return Path.Combine(_cacheDir, hash + ".png");
            }
        }

        /// <summary>
        /// Enqueue thumbnail generation for a file if not already present. The callback is invoked on completion
        /// with the path to the generated thumbnail (or null if generation failed).
        /// </summary>
        public void EnqueueGenerate(string filePath, Func<string, Task> onComplete, int width = 300, int height = 400)
        {
            if (string.IsNullOrWhiteSpace(filePath) || onComplete == null) return;
            if (_tasks.ContainsKey(filePath)) return;

            var t = Task.Run(async () =>
            {
                await _semaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    var result = await GenerateThumbAsync(filePath, width, height).ConfigureAwait(false);
                    try { await onComplete(result).ConfigureAwait(false); } catch (Exception ex) { Logger.LogException("ThumbnailManager.EnqueueGenerate - onComplete callback failed", ex); }
                }
                finally
                {
                    _semaphore.Release();
                    _tasks.TryRemove(filePath, out _);
                }
            });

            _tasks.TryAdd(filePath, t);
        }

        private async Task<string> GenerateThumbAsync(string filePath, int width, int height)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return null;
                if (!File.Exists(filePath) && !Directory.Exists(filePath)) return null;

                // Use ComicPageLoader if available; fall back to BitmapFrame attempt if not.
                BitmapSource cover = null;
                try
                {
                    using (var loader = new ComicReader.Services.ComicPageLoader(filePath))
                    {
                        await loader.LoadComicAsync().ConfigureAwait(false);
                        cover = await loader.GetCoverThumbnailAsync(width, height).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) { Logger.LogException("ThumbnailManager.GenerateThumbAsync - ComicPageLoader failed", ex); }

                if (cover == null)
                {
                    return null;
                }

                var path = ComputePath(filePath);
                try
                {
                    using (var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(cover));
                        enc.Save(fs);
                    }

                    // enforce cache size (simple LRU by file LastWriteTime)
                    try { EnforceCacheLimit(); } catch (Exception ex) { Logger.LogException("ThumbnailManager.GenerateThumbAsync - EnforceCacheLimit failed", ex); }

                    return path;
                }
                catch (Exception ex) { Logger.LogException("ThumbnailManager.GenerateThumbAsync - writing thumbnail failed", ex); return null; }
            }
            catch { return null; }
        }

        /// <summary>
        /// Enforce maximum number of files in the cache by deleting the oldest files.
        /// Safe to call multiple times.
        /// </summary>
        public void EnforceCacheLimit()
        {
            try
            {
                var files = new DirectoryInfo(_cacheDir).GetFiles("*.png").OrderBy(f => f.LastWriteTimeUtc).ToList();
                if (files.Count == 0) return;

                // Enforce by count (delete oldest until count <= max)
                int safety = 0;
                while (true)
                {
                    files = new DirectoryInfo(_cacheDir).GetFiles("*.png").OrderBy(f => f.LastWriteTimeUtc).ToList();
                    if (files.Count <= _maxCacheFiles) break;
                    // delete the oldest file
                    var oldest = files.FirstOrDefault();
                    if (oldest == null) break;
                    TryDeleteFile(oldest);
                    safety++;
                    if (safety > 1000) break; // avoid runaway
                }

                // Enforce by total size (delete oldest until total <= max)
                safety = 0;
                while (true)
                {
                    files = new DirectoryInfo(_cacheDir).GetFiles("*.png").OrderBy(f => f.LastWriteTimeUtc).ToList();
                    long total = files.Sum(f => f.Length);
                    if (total <= _maxCacheBytes) break;
                    var oldest = files.FirstOrDefault();
                    if (oldest == null) break;
                    TryDeleteFile(oldest);
                    safety++;
                    if (safety > 1000) break;
                }

                // Final aggressive cleanup: if count still exceeds max, delete all but newest N
                try
                {
                    files = new DirectoryInfo(_cacheDir).GetFiles("*.png").OrderBy(f => f.LastWriteTimeUtc).ToList();
                    if (files.Count > _maxCacheFiles)
                    {
                        var keep = files.OrderByDescending(f => f.LastWriteTimeUtc).Take(_maxCacheFiles).Select(f => f.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        var toDelete = files.Select(f => f.FullName).Where(p => !keep.Contains(p)).ToList();
                        foreach (var p in toDelete)
                        {
                            try { File.SetAttributes(p, FileAttributes.Normal); } catch (Exception ex) { Logger.LogException($"ThumbnailManager.EnforceCacheLimit - set attrs failed for {p}", ex); }
                            try { File.Delete(p); } catch (Exception ex) { Logger.LogException($"ThumbnailManager.EnforceCacheLimit - delete failed for {p}", ex); }
                            try
                            {
                                if (File.Exists(p))
                                {
                                    using (var fs = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                                    try { File.Delete(p); } catch (Exception ex) { Logger.LogException($"ThumbnailManager.EnforceCacheLimit - final delete failed for {p}", ex); }
                                }
                            }
                            catch (Exception ex) { Logger.LogException($"ThumbnailManager.EnforceCacheLimit - cleanup failed for {p}", ex); }
                        }
                    }
                }
                catch (Exception ex) { Logger.LogException("ThumbnailManager.EnforceCacheLimit - final pass failed", ex); }
            }
            catch { }
        }

        private void TryDeleteFile(FileInfo f)
        {
            try
            {
                if (f == null) return;
                try { f.IsReadOnly.ToString(); } catch (Exception ex) { Logger.LogException("ThumbnailManager.TryDeleteFile - IsReadOnly probe failed", ex); }
                try { File.SetAttributes(f.FullName, FileAttributes.Normal); } catch (Exception ex) { Logger.LogException($"ThumbnailManager.TryDeleteFile - SetAttributes failed for {f.FullName}", ex); }
                try { File.Delete(f.FullName); } catch (Exception ex) { Logger.LogException($"ThumbnailManager.TryDeleteFile - Delete failed for {f.FullName}", ex); }
            }
            catch (Exception ex) { Logger.LogException("ThumbnailManager.TryDeleteFile - unexpected error", ex); }
        }

        /// <summary>
        /// Clears entire thumbnail cache.
        /// </summary>
        public void ClearCache()
        {
            try
            {
                foreach (var f in new DirectoryInfo(_cacheDir).GetFiles("*.png"))
                {
                    try { f.Delete(); } catch (Exception ex) { Logger.LogException($"ThumbnailManager.ClearCache - delete failed for {f.FullName}", ex); }
                }
            }
            catch (Exception ex) { Logger.LogException("ThumbnailManager.ClearCache - failed", ex); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _semaphore?.Dispose();
            _disposed = true;
        }
    }
}
