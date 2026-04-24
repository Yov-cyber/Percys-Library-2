using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComicReader.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ComicReader.Services
{
    /// <summary>
    /// Renderizador de imágenes grandes usando tiles para optimizar memoria
    /// Divide imágenes >100MB en chunks manejables
    /// </summary>
    public sealed class TiledImageRenderer
    {
        private static readonly Lazy<TiledImageRenderer> _instance = 
            new Lazy<TiledImageRenderer>(() => new TiledImageRenderer());
        
        public static TiledImageRenderer Instance => _instance.Value;

        // Configuración
        public int TileSize { get; set; } = 2048; // Tamaño de cada tile en píxeles
        public long MaxMemoryMb { get; set; } = 500; // Memoria máxima para tiles en caché
        public int MaxCachedTiles { get; set; } = 50; // Máximo de tiles en caché

        // Umbrales para activar tiling
        private const long LargeImageThresholdBytes = 100 * 1024 * 1024; // 100MB
        private const int LargeImageDimensionThreshold = 8192; // 8K píxeles

        // Caché de tiles
        private readonly Dictionary<string, TileCache> _tileCache = new Dictionary<string, TileCache>();
        private readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);

        private TiledImageRenderer()
        {
            ModernLogger.Info("✓ TiledImageRenderer inicializado");
        }

        /// <summary>
        /// Verifica si una imagen requiere renderizado en tiles
        /// </summary>
        public bool RequiresTiling(string imagePath)
        {
            try
            {
                var fileInfo = new System.IO.FileInfo(imagePath);
                if (!fileInfo.Exists) return false;

                // Verificar tamaño de archivo
                if (fileInfo.Length > LargeImageThresholdBytes)
                {
                    return true;
                }

                // Verificar dimensiones
                using (var image = SixLabors.ImageSharp.Image.Load<Rgba32>(imagePath))
                {
                    if (image.Width > LargeImageDimensionThreshold || 
                        image.Height > LargeImageDimensionThreshold)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error verificando tiling: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Renderiza una imagen grande usando tiles
        /// </summary>
        public async Task<ImageSource> RenderTiledAsync(
            string imagePath, 
            Canvas targetCanvas,
            CancellationToken cancellationToken = default)
        {
            try
            {
                ModernLogger.Info($"🖼 Iniciando renderizado tiled: {System.IO.Path.GetFileName(imagePath)}");

                // Cargar metadata de la imagen
                ImageInfo imageInfo;
                using (var image = await Task.Run(() => 
                    SixLabors.ImageSharp.Image.Load<Rgba32>(imagePath), cancellationToken))
                {
                    imageInfo = new ImageInfo
                    {
                        Path = imagePath,
                        Width = image.Width,
                        Height = image.Height,
                        TileSize = TileSize
                    };
                }

                // Calcular cuántos tiles necesitamos
                var tilesX = (int)Math.Ceiling((double)imageInfo.Width / TileSize);
                var tilesY = (int)Math.Ceiling((double)imageInfo.Height / TileSize);
                var totalTiles = tilesX * tilesY;

                ModernLogger.Info($"📐 Imagen: {imageInfo.Width}x{imageInfo.Height}px, " +
                                $"Tiles: {tilesX}x{tilesY} = {totalTiles}");

                // Crear o obtener caché para esta imagen
                await _cacheLock.WaitAsync(cancellationToken);
                try
                {
                    if (!_tileCache.ContainsKey(imagePath))
                    {
                        _tileCache[imagePath] = new TileCache(imageInfo);
                    }
                }
                finally
                {
                    _cacheLock.Release();
                }

                // Limpiar canvas
                targetCanvas.Children.Clear();
                targetCanvas.Width = imageInfo.Width;
                targetCanvas.Height = imageInfo.Height;

                // Renderizar tiles visibles (empezar con los del centro/viewport)
                var visibleTiles = CalculateVisibleTiles(targetCanvas, tilesX, tilesY);
                
                foreach (var (x, y) in visibleTiles)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    
                    await RenderTileAsync(imagePath, x, y, imageInfo, targetCanvas, cancellationToken);
                }

                ModernLogger.Info($"✓ Renderizado tiled completado: {visibleTiles.Count}/{totalTiles} tiles");

                // Retornar un placeholder para compatibilidad
                return CreatePlaceholderImage(imageInfo.Width, imageInfo.Height);
            }
            catch (OperationCanceledException)
            {
                ModernLogger.Info("⚠ Renderizado tiled cancelado");
                throw;
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error en renderizado tiled: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Renderiza un tile específico
        /// </summary>
        private async Task RenderTileAsync(
            string imagePath,
            int tileX, 
            int tileY,
            ImageInfo imageInfo,
            Canvas canvas,
            CancellationToken cancellationToken)
        {
            try
            {
                var tileKey = $"{tileX}_{tileY}";
                
                // Verificar caché
                await _cacheLock.WaitAsync(cancellationToken);
                BitmapSource cachedTile = null;
                try
                {
                    if (_tileCache.TryGetValue(imagePath, out var cache))
                    {
                        cachedTile = cache.GetTile(tileKey);
                    }
                }
                finally
                {
                    _cacheLock.Release();
                }

                BitmapSource tileBitmap;
                
                if (cachedTile != null)
                {
                    tileBitmap = cachedTile;
                }
                else
                {
                    // Cargar y crear el tile
                    tileBitmap = await Task.Run(() =>
                    {
                        using (var image = SixLabors.ImageSharp.Image.Load<Rgba32>(imagePath))
                        {
                            var x = tileX * TileSize;
                            var y = tileY * TileSize;
                            var width = Math.Min(TileSize, imageInfo.Width - x);
                            var height = Math.Min(TileSize, imageInfo.Height - y);

                            // Extraer tile
                            using (var tileImage = image.Clone(ctx => 
                                ctx.Crop(new Rectangle(x, y, width, height))))
                            {
                                return ConvertToWpfBitmap(tileImage);
                            }
                        }
                    }, cancellationToken);

                    // Guardar en caché
                    await _cacheLock.WaitAsync(cancellationToken);
                    try
                    {
                        if (_tileCache.TryGetValue(imagePath, out var cache))
                        {
                            cache.SetTile(tileKey, tileBitmap);
                        }
                    }
                    finally
                    {
                        _cacheLock.Release();
                    }
                }

                // Agregar al canvas
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var imageControl = new System.Windows.Controls.Image
                    {
                        Source = tileBitmap,
                        Width = tileBitmap.PixelWidth,
                        Height = tileBitmap.PixelHeight,
                        Stretch = Stretch.None
                    };

                    Canvas.SetLeft(imageControl, tileX * TileSize);
                    Canvas.SetTop(imageControl, tileY * TileSize);
                    Canvas.SetZIndex(imageControl, 0);

                    canvas.Children.Add(imageControl);
                });
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error renderizando tile ({tileX},{tileY}): {ex.Message}");
            }
        }

        /// <summary>
        /// Calcula qué tiles están visibles en el viewport
        /// </summary>
        private List<(int x, int y)> CalculateVisibleTiles(Canvas canvas, int tilesX, int tilesY)
        {
            var visibleTiles = new List<(int x, int y)>();
            
            // Por ahora renderizar todos (en una implementación real, 
            // solo renderizaríamos los visibles en el viewport)
            for (int y = 0; y < tilesY; y++)
            {
                for (int x = 0; x < tilesX; x++)
                {
                    visibleTiles.Add((x, y));
                }
            }

            return visibleTiles;
        }

        /// <summary>
        /// Convierte una imagen de ImageSharp a WPF BitmapSource
        /// </summary>
        private BitmapSource ConvertToWpfBitmap(SixLabors.ImageSharp.Image<Rgba32> image)
        {
            var width = image.Width;
            var height = image.Height;
            var pixels = new byte[width * height * 4];

            image.CopyPixelDataTo(pixels);

            var bitmap = BitmapSource.Create(
                width, height,
                96, 96,
                PixelFormats.Bgra32,
                null,
                pixels,
                width * 4);

            bitmap.Freeze(); // Hacer thread-safe
            return bitmap;
        }

        /// <summary>
        /// Crea una imagen placeholder
        /// </summary>
        private ImageSource CreatePlaceholderImage(int width, int height)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
            }

            var rtb = new RenderTargetBitmap(1, 1, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>
        /// Limpia la caché de tiles
        /// </summary>
        public async Task ClearCacheAsync()
        {
            await _cacheLock.WaitAsync();
            try
            {
                _tileCache.Clear();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                ModernLogger.Info("🧹 Caché de tiles limpiada");
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        /// <summary>
        /// Limpia caché de una imagen específica
        /// </summary>
        public async Task ClearCacheForImageAsync(string imagePath)
        {
            await _cacheLock.WaitAsync();
            try
            {
                if (_tileCache.Remove(imagePath))
                {
                    ModernLogger.Info($"🧹 Caché limpiada para: {System.IO.Path.GetFileName(imagePath)}");
                }
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        // ============================================================
        // CLASES INTERNAS
        // ============================================================

        private class ImageInfo
        {
            public string Path { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int TileSize { get; set; }
        }

        private class TileCache
        {
            private readonly ImageInfo _imageInfo;
            private readonly Dictionary<string, BitmapSource> _tiles = new Dictionary<string, BitmapSource>();
            private readonly Queue<string> _accessOrder = new Queue<string>();

            public TileCache(ImageInfo imageInfo)
            {
                _imageInfo = imageInfo;
            }

            public BitmapSource GetTile(string key)
            {
                if (_tiles.TryGetValue(key, out var tile))
                {
                    return tile;
                }
                return null;
            }

            public void SetTile(string key, BitmapSource tile)
            {
                if (!_tiles.ContainsKey(key))
                {
                    _tiles[key] = tile;
                    _accessOrder.Enqueue(key);

                    // Limitar tamaño de caché
                    if (_tiles.Count > 50) // MaxCachedTiles
                    {
                        var oldestKey = _accessOrder.Dequeue();
                        _tiles.Remove(oldestKey);
                    }
                }
            }
        }
    }
}
