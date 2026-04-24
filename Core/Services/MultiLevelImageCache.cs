using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Media.Imaging;
using ComicReader.Core.Abstractions;
using System.Text.Json;

namespace ComicReader.Core.Services
{
    /// <summary>
    /// Hybrid multi-level image cache:
    /// - In-memory LRU cache with byte-size accounting and eviction.
    /// - On-disk cache with index file to maintain LRU and total disk size limit.
    /// - Background persistence and safe write (temp-then-move).
    /// - Async Get/Set APIs compatible with IImageCache.
    /// </summary>
    public class MultiLevelImageCache : IImageCache, IDisposable
    {
        private readonly LruMemoryCache _memory;
        private readonly string _diskPath;
        private readonly object _diskLock = new();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly BlockingCollection<Func<Task>> _bgQueue = new();
        private readonly Task _bgWorker;
        private readonly long _diskLimitBytes;
        private readonly string _indexFile;
    private readonly Dictionary<string, DiskIndexEntry> _diskIndex = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pinnedKeys = new(StringComparer.Ordinal);
    // Metrics
    private long _memoryHits = 0;
    private long _memoryMisses = 0;
    private long _diskHits = 0;
    private long _diskMisses = 0;
    private long _memorySets = 0;
    private long _diskSets = 0;

        private class DiskIndexEntry
        {
            public string FileName { get; set; }
            public long Size { get; set; }
            public bool Pinned { get; set; }
            public long LastAccessUtcTicks { get; set; }
        }

        /// <summary>
        /// Create a new MultiLevelImageCache.
        /// memoryItemLimit: approximate number of items to keep in memory (fallback, used to compute default byte limit if memoryByteLimit==0)
        /// memoryByteLimit: exact memory byte cap (0 -> will be computed from memoryItemLimit)
        /// diskFolder: path for disk cache (default LocalAppData\PercysLibrary\Cache)
        /// diskLimitMb: maximum disk usage for cache in MB
        /// </summary>
        public MultiLevelImageCache(int memoryItemLimit = 200, long memoryByteLimit = 0, string diskFolder = null, int diskLimitMb = 1024)
        {
            if (memoryByteLimit <= 0)
            {
                // approximate 4 bytes per pixel * 800x1200 average ~ 3.84MB per image; conservative estimate 500KB per image
                memoryByteLimit = Math.Max(10 * 1024 * 1024, memoryItemLimit * 512 * 1024L);
            }
            _memory = new LruMemoryCache(memoryByteLimit);
            _diskPath = diskFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PercysLibrary", "Cache");
            Directory.CreateDirectory(_diskPath);
            _indexFile = Path.Combine(_diskPath, "index.json");
            _diskLimitBytes = Math.Max(10L * 1024 * 1024, diskLimitMb * 1024L * 1024L);
            LoadIndex();
            _bgWorker = Task.Run(ProcessQueueAsync);
        }

        public async Task<BitmapImage> Get(string key)
        {
            // Try memory
            if (_memory.TryGet(key, out var img))
            {
                Interlocked.Increment(ref _memoryHits);
                return img;
            }
            Interlocked.Increment(ref _memoryMisses);

            // Try disk
            var file = Path.Combine(_diskPath, SafeFileName(key) + ".png");
            lock (_diskLock)
            {
                if (_diskIndex.TryGetValue(key, out var entry))
                {
                    // update last access
                    entry.LastAccessUtcTicks = DateTime.UtcNow.Ticks;
                }
            }

            if (File.Exists(file))
            {
                try
                {
                    var bmp = await Task.Run(() =>
                    {
                        try
                        {
                            var local = new BitmapImage();
                            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                            local.BeginInit();
                            local.CacheOption = BitmapCacheOption.OnLoad;
                            local.StreamSource = fs;
                            local.EndInit();
                            local.Freeze();
                            return local;
                        }
                        catch { return null; }
                    }).ConfigureAwait(false);

                    if (bmp != null)
                    {
                        Interlocked.Increment(ref _diskHits);
                        _memory.Set(key, bmp);
                        Interlocked.Increment(ref _memorySets);
                        // update index asynchronously
                        QueueBackground(async () => { SaveIndex(); await Task.CompletedTask; });
                        EnforceDiskLimitAsync();
                        return bmp;
                    }
                    Interlocked.Increment(ref _diskMisses);
                }
                catch { }
            }
            Interlocked.Increment(ref _diskMisses);
            return null;
        }

        public Task Set(string key, BitmapImage image)
        {
            if (image == null) return Task.CompletedTask;
            _memory.Set(key, image);
            Interlocked.Increment(ref _memorySets);
            // persist to disk in background
            QueueBackground(() => PersistToDiskAsync(key, image));
            return Task.CompletedTask;
        }

        public void PurgeMemory()
        {
            _memory.Clear();
        }

        private void EnforceMemoryLimit()
        {
            _memory.EnforceLimit();
        }

        private void LoadIndex()
        {
            lock (_diskLock)
            {
                try
                {
                    if (File.Exists(_indexFile))
                    {
                        var json = File.ReadAllText(_indexFile);
                        var data = JsonSerializer.Deserialize<Dictionary<string, DiskIndexEntry>>(json);
                        if (data != null)
                        {
                            foreach (var kv in data)
                                _diskIndex[kv.Key] = kv.Value;
                        }
                    }
                    else
                    {
                        // Try to scan existing files
                        foreach (var f in Directory.GetFiles(_diskPath, "*.png"))
                        {
                            var fi = new FileInfo(f);
                            var name = Path.GetFileNameWithoutExtension(f);
                            // We cannot reliably map filename back to key if SafeFileName truncated; accept best-effort
                            _diskIndex[name] = new DiskIndexEntry { FileName = Path.GetFileName(f), Size = fi.Length, LastAccessUtcTicks = fi.LastAccessTimeUtc.Ticks };
                        }
                        SaveIndex();
                    }
                }
                catch { /* ignore index load errors */ }
            }
        }

        private void SaveIndex()
        {
            lock (_diskLock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(_diskIndex);
                    File.WriteAllText(_indexFile, json);
                }
                catch { }
            }
        }

        private Task PersistToDiskAsync(string key, BitmapImage image)
        {
            return Task.Run(() =>
            {
                try
                {
                    var fileName = SafeFileName(key) + ".png";
                    var tmp = Path.Combine(_diskPath, fileName + ".tmp");
                    var final = Path.Combine(_diskPath, fileName);
                    lock (_diskLock)
                    {
                        if (File.Exists(final))
                        {
                            // update index timestamp
                            if (_diskIndex.TryGetValue(key, out var e))
                            {
                                e.LastAccessUtcTicks = DateTime.UtcNow.Ticks;
                            }
                            else
                            {
                                var fi = new FileInfo(final);
                                _diskIndex[key] = new DiskIndexEntry { FileName = Path.GetFileName(final), Size = fi.Length, LastAccessUtcTicks = DateTime.UtcNow.Ticks, Pinned = _pinnedKeys.Contains(key) };
                            }
                            SaveIndex();
                            Interlocked.Increment(ref _diskSets);
                            return;
                        }
                    }

                    // write temp file
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        encoder.Save(fs);
                    }
                    // move to final atomically
                    lock (_diskLock)
                    {
                        if (File.Exists(final))
                        {
                            try { File.Delete(tmp); } catch { }
                        }
                        else
                        {
                            File.Move(tmp, final);
                            var fi = new FileInfo(final);
                            _diskIndex[key] = new DiskIndexEntry { FileName = Path.GetFileName(final), Size = fi.Length, LastAccessUtcTicks = DateTime.UtcNow.Ticks, Pinned = _pinnedKeys.Contains(key) };
                            Interlocked.Increment(ref _diskSets);
                            SaveIndex();
                        }
                    }
                    EnforceDiskLimitAsync();
                }
                catch { /* ignore persist errors */ }
            }, _cts.Token);
        }

        private void EnforceDiskLimitAsync()
        {
            QueueBackground(async () =>
            {
                try
                {
                    List<KeyValuePair<string, DiskIndexEntry>> items;
                    lock (_diskLock)
                    {
                        items = _diskIndex.ToList();
                    }
                    long total = items.Sum(i => i.Value?.Size ?? 0);
                    if (total <= _diskLimitBytes) return;
                    // remove oldest accessed first
                    var ordered = items.OrderBy(i => i.Value.LastAccessUtcTicks).ToList();
                    foreach (var kv in ordered)
                    {
                        if (total <= _diskLimitBytes) break;
                        // skip pinned entries
                        if (kv.Value.Pinned) continue;
                        try
                        {
                            var f = Path.Combine(_diskPath, kv.Value.FileName);
                            if (File.Exists(f))
                            {
                                var fi = new FileInfo(f);
                                long s = fi.Length;
                                File.Delete(f);
                                total -= s;
                            }
                        }
                        catch { }
                        lock (_diskLock) { _diskIndex.Remove(kv.Key); }
                    }
                    SaveIndex();
                }
                catch { }
                await Task.CompletedTask;
            });
        }

        private void QueueBackground(Func<Task> work)
        {
            if (_cts.IsCancellationRequested) return;
            try { _bgQueue.Add(work, _cts.Token); } catch { }
        }

        private async Task ProcessQueueAsync()
        {
            try
            {
                foreach (var work in _bgQueue.GetConsumingEnumerable(_cts.Token))
                {
                    try { await work().ConfigureAwait(false); } catch { }
                }
            }
            catch (OperationCanceledException) { }
        }

        private string SafeFileName(string key)
        {
            try
            {
                if (string.IsNullOrEmpty(key)) return "_";
                using var sha = System.Security.Cryptography.SHA256.Create();
                var data = System.Text.Encoding.UTF8.GetBytes(key);
                var hash = sha.ComputeHash(data);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
            catch
            {
                var name = key ?? "";
                foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                if (name.Length > 120) name = name.Substring(0, 120);
                return name;
            }
        }

        // Pin/unpin keys to prevent eviction
        public void Pin(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_diskLock)
            {
                _pinnedKeys.Add(key);
                if (_diskIndex.TryGetValue(key, out var e)) { e.Pinned = true; }
                SaveIndex();
            }
            try { _memory.Pin(key); } catch { }
        }

        public void Unpin(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_diskLock)
            {
                _pinnedKeys.Remove(key);
                if (_diskIndex.TryGetValue(key, out var e)) { e.Pinned = false; }
                SaveIndex();
            }
            try { _memory.Unpin(key); } catch { }
        }

        public (long memoryHits, long memoryMisses, long diskHits, long diskMisses, long memorySets, long diskSets) GetMetrics()
        {
            return (Interlocked.Read(ref _memoryHits), Interlocked.Read(ref _memoryMisses), Interlocked.Read(ref _diskHits), Interlocked.Read(ref _diskMisses), Interlocked.Read(ref _memorySets), Interlocked.Read(ref _diskSets));
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { _bgQueue.CompleteAdding(); } catch { }
            try { _bgWorker.Wait(1000); } catch { }
            try { _cts.Dispose(); } catch { }
            // save index on shutdown
            try { SaveIndex(); } catch { }
        }

        // --- internal LRU memory cache implementation ---
        private class LruMemoryCache
        {
            private readonly long _byteLimit;
            private readonly object _sync = new object();
            private readonly Dictionary<string, LinkedListNode<MemoryNode>> _map = new(StringComparer.Ordinal);
            private readonly LinkedList<MemoryNode> _list = new();
            private readonly HashSet<string> _pinned = new(StringComparer.Ordinal);
            private long _currentBytes = 0;

            private class MemoryNode
            {
                public string Key { get; set; }
                public BitmapImage Image { get; set; }
                public long SizeBytes { get; set; }
            }

            public LruMemoryCache(long byteLimit)
            {
                _byteLimit = Math.Max(1, byteLimit);
            }

            public bool TryGet(string key, out BitmapImage image)
            {
                lock (_sync)
                {
                    if (_map.TryGetValue(key, out var node))
                    {
                        _list.Remove(node);
                        _list.AddFirst(node);
                        image = node.Value.Image;
                        return true;
                    }
                }
                image = null;
                return false;
            }

            public void Set(string key, BitmapImage image)
            {
                if (image == null) return;
                var size = EstimateImageSize(image);
                lock (_sync)
                {
                    if (_map.TryGetValue(key, out var existing))
                    {
                        _list.Remove(existing);
                        _currentBytes -= existing.Value.SizeBytes;
                        _map.Remove(key);
                    }
                    var node = new MemoryNode { Key = key, Image = image, SizeBytes = size };
                    var ln = new LinkedListNode<MemoryNode>(node);
                    _list.AddFirst(ln);
                    _map[key] = ln;
                    _currentBytes += size;
                }
                EnforceLimit();
            }

            public void EnforceLimit()
            {
                lock (_sync)
                {
                    while (_currentBytes > _byteLimit && _list.Count > 0)
                    {
                        // Find last non-pinned node
                        var node = _list.Last;
                        while (node != null && _pinned.Contains(node.Value.Key)) node = node.Previous;
                        if (node == null) break; // nothing we can evict (all pinned)
                        _list.Remove(node);
                        if (_map.Remove(node.Value.Key))
                        {
                            _currentBytes -= node.Value.SizeBytes;
                        }
                    }
                }
            }

            public void Pin(string key)
            {
                if (string.IsNullOrEmpty(key)) return;
                lock (_sync) { _pinned.Add(key); }
            }

            public void Unpin(string key)
            {
                if (string.IsNullOrEmpty(key)) return;
                lock (_sync) { _pinned.Remove(key); }
            }

            public void Clear()
            {
                lock (_sync)
                {
                    _map.Clear();
                    _list.Clear();
                    _currentBytes = 0;
                }
            }

            private static long EstimateImageSize(BitmapImage bmp)
            {
                try
                {
                    // Use pixel size * 4 (ARGB) as estimate
                    var px = (long)bmp.PixelWidth * bmp.PixelHeight;
                    var bpp = 4;
                    return Math.Max(1024, px * bpp);
                }
                catch { return 256 * 1024; }
            }
        }
    }
}
