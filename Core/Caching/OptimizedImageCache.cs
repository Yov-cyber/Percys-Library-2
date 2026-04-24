// FileName: Core/Caching/OptimizedImageCache.cs
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Caching.Memory;
using K4os.Compression.LZ4;

namespace ComicReader.Core.Caching
{
    /// <summary>
    /// Sistema de cache optimizado multi-nivel con compresión LZ4 y gestión inteligente de memoria.
    /// Proporciona almacenamiento en memoria y disco con precarga anticipada.
    /// </summary>
    public class OptimizedImageCache : IDisposable
    {
        private readonly MemoryCache _memoryCache;
        private readonly string _diskCachePath;
        private readonly SemaphoreSlim _diskLock = new(1, 1);
        private readonly ConcurrentDictionary<string, Task<BitmapImage>> _ongoingLoads = new();
        
        // Configuración optimizada
        private const long MaxMemoryCacheSize = 512 * 1024 * 1024; // 512 MB en memoria
        private const long MaxDiskCacheSize = 2L * 1024 * 1024 * 1024; // 2 GB en disco
        private static readonly K4os.Compression.LZ4.LZ4Level CompressionLevel = K4os.Compression.LZ4.LZ4Level.L00_FAST; // Compresión ultra-rápida

        public OptimizedImageCache(string diskCachePath = null)
        {
            _diskCachePath = diskCachePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PercysLibrary", "ImageCache");

            Directory.CreateDirectory(_diskCachePath);

            _memoryCache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = MaxMemoryCacheSize,
                ExpirationScanFrequency = TimeSpan.FromMinutes(5)
            });
        }

        /// <summary>
        /// Obtiene una imagen del cache o la carga si no existe.
        /// </summary>
        public async Task<BitmapImage> GetOrLoadAsync(
            string key, 
            Func<Task<BitmapImage>> loader, 
            CancellationToken ct = default)
        {
            // 1. Verificar memoria cache
            if (_memoryCache.TryGetValue<BitmapImage>(key, out var cached))
            {
                return cached;
            }

            // 2. Verificar disco cache
            var diskPath = GetDiskPath(key);
            if (File.Exists(diskPath))
            {
                try
                {
                    var image = await LoadFromDiskAsync(diskPath, ct);
                    if (image != null)
                    {
                        CacheInMemory(key, image);
                        return image;
                    }
                }
                catch { /* Si falla la carga de disco, recargar */ }
            }

            // 3. Deduplicar cargas concurrentes
            var loadTask = _ongoingLoads.GetOrAdd(key, _ => Task.Run(async () =>
            {
                try
                {
                    var image = await loader();
                    if (image != null)
                    {
                        // Guardar en ambos niveles de cache
                        CacheInMemory(key, image);
                        await SaveToDiskAsync(key, image, ct);
                    }
                    return image;
                }
                finally
                {
                    _ongoingLoads.TryRemove(key, out Task<BitmapImage> _);
                }
            }, ct));

            return await loadTask;
        }

        /// <summary>
        /// Precarga múltiples imágenes en segundo plano.
        /// </summary>
        public async Task PreloadAsync(
            string[] keys, 
            Func<string, Task<BitmapImage>> loader,
            int maxConcurrency = 4,
            CancellationToken ct = default)
        {
            using var semaphore = new SemaphoreSlim(maxConcurrency);
            var tasks = new List<Task>();

            foreach (var key in keys)
            {
                if (ct.IsCancellationRequested) break;

                var task = Task.Run(async () =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        await GetOrLoadAsync(key, () => loader(key), ct);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, ct);

                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
        }

        private void CacheInMemory(string key, BitmapImage image)
        {
            if (image == null) return;

            // Calcular tamaño aproximado en bytes
            long size = EstimateImageSize(image);

            var options = new MemoryCacheEntryOptions
            {
                Size = size,
                Priority = CacheItemPriority.Normal,
                SlidingExpiration = TimeSpan.FromMinutes(30)
            };

            _memoryCache.Set(key, image, options);
        }

        private async Task SaveToDiskAsync(string key, BitmapImage image, CancellationToken ct)
        {
            if (image == null) return;

            await _diskLock.WaitAsync(ct);
            try
            {
                var diskPath = GetDiskPath(key);
                Directory.CreateDirectory(Path.GetDirectoryName(diskPath));

                // Codificar a PNG y comprimir con LZ4
                using var ms = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(ms);

                var uncompressed = ms.ToArray();
                var compressed = LZ4Pickler.Pickle(uncompressed, CompressionLevel);

                await File.WriteAllBytesAsync(diskPath, compressed, ct);

                // Limpiar cache de disco si excede el límite
                await CleanupDiskCacheIfNeeded();
            }
            catch (Exception ex)
            {
                ComicReader.Services.Logger.LogException($"Error saving to disk cache: {key}", ex);
            }
            finally
            {
                _diskLock.Release();
            }
        }

        private async Task<BitmapImage> LoadFromDiskAsync(string diskPath, CancellationToken ct)
        {
            await _diskLock.WaitAsync(ct);
            try
            {
                var compressed = await File.ReadAllBytesAsync(diskPath, ct);
                var uncompressed = LZ4Pickler.Unpickle(compressed);

                using var ms = new MemoryStream(uncompressed);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze();

                return image;
            }
            finally
            {
                _diskLock.Release();
            }
        }

        private async Task CleanupDiskCacheIfNeeded()
        {
            try
            {
                var files = Directory.GetFiles(_diskCachePath, "*", SearchOption.AllDirectories);
                long totalSize = 0;
                var fileInfos = new List<(string path, long size, DateTime lastAccess)>();

                foreach (var file in files)
                {
                    var info = new FileInfo(file);
                    totalSize += info.Length;
                    fileInfos.Add((file, info.Length, info.LastAccessTime));
                }

                if (totalSize > MaxDiskCacheSize)
                {
                    // Eliminar los archivos más antiguos
                    var toDelete = fileInfos
                        .OrderBy(f => f.lastAccess)
                        .Take((int)(fileInfos.Count * 0.3)); // Eliminar 30% más antiguos

                    foreach (var file in toDelete)
                    {
                        try
                        {
                            File.Delete(file.path);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                ComicReader.Services.Logger.LogException("Error cleaning disk cache", ex);
            }

            await Task.CompletedTask;
        }

        private string GetDiskPath(string key)
        {
            // Crear hash seguro del key para nombre de archivo
            var hash = Convert.ToBase64String(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(key)))
                .Replace('/', '_')
                .Replace('+', '-')
                .Substring(0, 32);

            return Path.Combine(_diskCachePath, $"{hash}.lz4");
        }

        private long EstimateImageSize(BitmapImage image)
        {
            if (image == null) return 0;

            // Estimación: ancho * alto * 4 bytes (RGBA)
            return image.PixelWidth * image.PixelHeight * 4L;
        }

        public void Clear()
        {
            _memoryCache.Clear();
            
            try
            {
                if (Directory.Exists(_diskCachePath))
                {
                    Directory.Delete(_diskCachePath, true);
                    Directory.CreateDirectory(_diskCachePath);
                }
            }
            catch (Exception ex)
            {
                ComicReader.Services.Logger.LogException("Error clearing disk cache", ex);
            }
        }

        public void Dispose()
        {
            _memoryCache?.Dispose();
            _diskLock?.Dispose();
        }
    }
}
