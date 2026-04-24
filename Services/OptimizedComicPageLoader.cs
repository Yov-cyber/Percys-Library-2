// FileName: Services/OptimizedComicPageLoader.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.Tar;
using SharpCompress.Archives.SevenZip;
using ComicReader.Models;
using ComicReader.Core.Caching;
using ComicReader.Core.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace ComicReader.Services
{
    /// <summary>
    /// Loader de páginas de cómic optimizado con precarga inteligente y cache multi-nivel.
    /// Garantiza que todas las imágenes se cargan completamente antes de mostrarse (sin blur).
    /// </summary>
    public class OptimizedComicPageLoader : IComicPageLoader, IDisposable
    {
        private readonly OptimizedImageCache _cache;
        private readonly SemaphoreSlim _decodeSemaphore;
        private readonly SemaphoreSlim _archiveSemaphore = new(3, 3);
        
        private string _filePath;
        private List<ComicPage> _pages = new();
        private CancellationTokenSource _cts = new();
        
        // Configuración optimizada
        private const int OptimalDecodeWidth = 2400; // 4K-ready, escala bien en pantallas HD
        private const int ThumbnailSize = 300;
        private const int PreloadWindowSize = 5; // Precargar 5 páginas adelante/atrás

        public List<ComicPage> Pages => _pages;
        public string FilePath => _filePath;
        public int PageCount => _pages.Count;
        public string ComicTitle => string.IsNullOrEmpty(_filePath) 
            ? "Cómic sin título" 
            : Path.GetFileNameWithoutExtension(_filePath);

        // Evento para notificar cuando la imagen completa está lista
        public event Action<int, BitmapImage> FullImageReady;

        public OptimizedComicPageLoader()
        {
            _cache = new OptimizedImageCache();
            _decodeSemaphore = new SemaphoreSlim(
                Math.Max(2, Environment.ProcessorCount / 2), 
                Math.Max(2, Environment.ProcessorCount / 2));
        }

        /// <summary>
        /// Carga el archivo de cómic y prepara la estructura de páginas.
        /// </summary>
        public async Task LoadComicAsync(string filePath = null)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            if (filePath != null)
            {
                _filePath = filePath;
            }
            _pages.Clear();

            if (!File.Exists(filePath) && !Directory.Exists(filePath))
            {
                throw new FileNotFoundException($"No se encontró: {filePath}");
            }

            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            try
            {
                if (Directory.Exists(filePath))
                {
                    await LoadFolderAsync(filePath);
                }
                else
                {
                    switch (ext)
                    {
                        case ".cbz":
                            await LoadCBZAsync();
                            break;
                        case ".cbr":
                            await LoadCBRAsync();
                            break;
                        case ".cbt":
                            await LoadCBTAsync();
                            break;
                        case ".cb7":
                            await LoadCB7Async();
                            break;
                        case ".pdf":
                            await LoadPDFAsync();
                            break;
                        case ".epub":
                            await LoadEPUBAsync();
                            break;
                        default:
                            throw new NotSupportedException($"Formato no soportado: {ext}");
                    }
                }

                if (_pages.Count == 0)
                {
                    throw new InvalidDataException("El cómic no contiene páginas válidas");
                }

                Logger.Log($"Cómic cargado: {_filePath} con {_pages.Count} páginas");
            }
            catch (Exception ex)
            {
                Logger.LogException($"Error cargando cómic: {filePath}", ex);
                throw;
            }
        }

        /// <summary>
        /// Obtiene una imagen de página completamente cargada (sin blur).
        /// </summary>
        public async Task<BitmapImage> GetPageImageAsync(
            int pageNumber, 
            int targetWidth = 0)
        {
            if (pageNumber < 0 || pageNumber >= _pages.Count)
            {
                return CreatePlaceholder("Página inválida");
            }

            var page = _pages[pageNumber];
            var cacheKey = $"page_{_filePath}_{pageNumber}_{targetWidth}";

            try
            {
                var image = await _cache.GetOrLoadAsync(
                    cacheKey,
                    async () => await LoadPageFromSourceAsync(pageNumber, targetWidth, _cts.Token),
                    _cts.Token);

                // Asegurar que la imagen está completamente decodificada y congelada
                if (image != null && !image.IsFrozen)
                {
                    image.Freeze();
                }

                // Disparar evento de imagen completa lista
                try
                {
                    FullImageReady?.Invoke(pageNumber, image);
                }
                catch { }

                return image ?? CreatePlaceholder("Error al cargar");
            }
            catch (Exception ex)
            {
                Logger.LogException($"Error obteniendo página {pageNumber}", ex);
                return CreatePlaceholder("Error");
            }
        }

        /// <summary>
        /// Obtiene una miniatura optimizada de la página.
        /// </summary>
        public async Task<BitmapImage> GetPageThumbnailAsync(
            int pageNumber,
            int width = 200,
            int height = 300)
        {
            if (pageNumber < 0 || pageNumber >= _pages.Count)
            {
                return CreatePlaceholder("Página inválida", ThumbnailSize, ThumbnailSize);
            }

            var cacheKey = $"thumb_{_filePath}_{pageNumber}";

            try
            {
                return await _cache.GetOrLoadAsync(
                    cacheKey,
                    async () => await LoadPageFromSourceAsync(pageNumber, ThumbnailSize, _cts.Token),
                    _cts.Token);
            }
            catch (Exception ex)
            {
                Logger.LogException($"Error obteniendo miniatura {pageNumber}", ex);
                return CreatePlaceholder("Error", ThumbnailSize, ThumbnailSize);
            }
        }

        /// <summary>
        /// Precarga páginas adyacentes para navegación fluida.
        /// </summary>
        public async Task PreloadAdjacentPagesAsync(
            int currentPage,
            CancellationToken ct = default)
        {
            var pagesToPreload = new List<int>();

            // Precargar ventana alrededor de la página actual
            for (int i = 1; i <= PreloadWindowSize; i++)
            {
                int prev = currentPage - i;
                int next = currentPage + i;

                if (prev >= 0) pagesToPreload.Add(prev);
                if (next < _pages.Count) pagesToPreload.Add(next);
            }

            if (pagesToPreload.Count == 0) return;

            // Precarga en paralelo con límite de concurrencia
            await _cache.PreloadAsync(
                pagesToPreload.Select(p => $"page_{_filePath}_{p}_0").ToArray(),
                key =>
                {
                    var pageNum = int.Parse(key.Split('_')[^2]);
                    return LoadPageFromSourceAsync(pageNum, 0, ct);
                },
                maxConcurrency: 4,
                ct);
        }

        // Métodos de compatibilidad con código legacy
        public void PreloadPages(int currentPage) => 
            Task.Run(() => PreloadAdjacentPagesAsync(currentPage, _cts.Token)).Wait();

        public void ClearCurrent() => Clear();

        public void RefreshTuningFromSettings() 
        {
            // No hay configuración dinámica en esta versión optimizada
            // La configuración se aplica al construir el loader
        }

        // Sobrecarga para compatibilidad con llamadas antiguas (4 parámetros)
        public async Task<BitmapImage> GetPageThumbnailAsync(
            int pageNumber, 
            int width, 
            int height, 
            CancellationToken ct)
        {
            // Ignorar height, usamos aspect ratio automático
            return await GetPageThumbnailAsync(pageNumber, width, height);
        }

        /// <summary>
        /// Carga completa del cómic en memoria (para experiencia sin lag).
        /// </summary>
        public async Task PreloadAllPagesAsync(
            IProgress<(int current, int total)> progress = null,
            CancellationToken ct = default)
        {
            var keys = Enumerable.Range(0, _pages.Count)
                .Select(i => $"page_{_filePath}_{i}_0")
                .ToArray();

            int completed = 0;
            await _cache.PreloadAsync(
                keys,
                async key =>
                {
                    var pageNum = int.Parse(key.Split('_')[^2]);
                    var image = await LoadPageFromSourceAsync(pageNum, 0, ct);
                    
                    Interlocked.Increment(ref completed);
                    progress?.Report((completed, _pages.Count));
                    
                    return image;
                },
                maxConcurrency: Environment.ProcessorCount,
                ct);
        }

        private async Task<BitmapImage> LoadPageFromSourceAsync(
            int pageNumber,
            int targetWidth,
            CancellationToken ct)
        {
            await _decodeSemaphore.WaitAsync(ct);
            try
            {
                return await Task.Run(() => LoadPageFromSourceSync(pageNumber, targetWidth), ct);
            }
            finally
            {
                _decodeSemaphore.Release();
            }
        }

        private BitmapImage LoadPageFromSourceSync(int pageNumber, int targetWidth)
        {
            if (pageNumber < 0 || pageNumber >= _pages.Count)
            {
                return CreatePlaceholder("Página inválida");
            }

            var page = _pages[pageNumber];
            var fileName = page.FileName;
            var ext = Path.GetExtension(_filePath).ToLowerInvariant();

            // Ancho óptimo: usar el solicitado o el predeterminado
            int decodeWidth = targetWidth > 0 ? targetWidth : OptimalDecodeWidth;

            Stream imageStream = null;

            try
            {
                // Obtener stream según el tipo de archivo
                if (Directory.Exists(_filePath))
                {
                    imageStream = File.OpenRead(fileName);
                }
                else
                {
                    imageStream = ExtractImageFromArchive(fileName);
                }

                if (imageStream == null)
                {
                    return CreatePlaceholder("No se pudo extraer");
                }

                // Decodificar con configuración óptima
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = decodeWidth; // WPF mantiene aspect ratio
                bitmap.StreamSource = imageStream;
                bitmap.EndInit();
                bitmap.Freeze(); // Importante: congelar para thread-safety

                return bitmap;
            }
            catch (Exception ex)
            {
                Logger.LogException($"Error decodificando página {pageNumber}", ex);
                return CreatePlaceholder("Error de decodificación");
            }
            finally
            {
                imageStream?.Dispose();
            }
        }

        private Stream ExtractImageFromArchive(string entryName)
        {
            var ext = Path.GetExtension(_filePath).ToLowerInvariant();

            _archiveSemaphore.Wait();
            try
            {
                switch (ext)
                {
                    case ".cbz":
                        using (var archive = ZipFile.OpenRead(_filePath))
                        {
                            var entry = archive.Entries
                                .FirstOrDefault(e => e.FullName.Equals(entryName, StringComparison.OrdinalIgnoreCase));
                            
                            if (entry != null)
                            {
                                var ms = new MemoryStream();
                                using (var entryStream = entry.Open())
                                {
                                    entryStream.CopyTo(ms);
                                }
                                ms.Position = 0;
                                return ms;
                            }
                        }
                        break;

                    case ".cbr":
                        using (var archive = RarArchive.Open(_filePath))
                        {
                            var entry = archive.Entries
                                .FirstOrDefault(e => e.Key.Equals(entryName, StringComparison.OrdinalIgnoreCase));
                            
                            if (entry != null)
                            {
                                var ms = new MemoryStream();
                                using (var entryStream = entry.OpenEntryStream())
                                {
                                    entryStream.CopyTo(ms);
                                }
                                ms.Position = 0;
                                return ms;
                            }
                        }
                        break;

                    case ".cbt":
                        using (var archive = TarArchive.Open(_filePath))
                        {
                            var entry = archive.Entries
                                .FirstOrDefault(e => e.Key.Equals(entryName, StringComparison.OrdinalIgnoreCase));
                            
                            if (entry != null)
                            {
                                var ms = new MemoryStream();
                                using (var entryStream = entry.OpenEntryStream())
                                {
                                    entryStream.CopyTo(ms);
                                }
                                ms.Position = 0;
                                return ms;
                            }
                        }
                        break;

                    case ".cb7":
                        using (var archive = SevenZipArchive.Open(_filePath))
                        {
                            var entry = archive.Entries
                                .FirstOrDefault(e => e.Key.Equals(entryName, StringComparison.OrdinalIgnoreCase));
                            
                            if (entry != null)
                            {
                                var ms = new MemoryStream();
                                using (var entryStream = entry.OpenEntryStream())
                                {
                                    entryStream.CopyTo(ms);
                                }
                                ms.Position = 0;
                                return ms;
                            }
                        }
                        break;
                }
            }
            finally
            {
                _archiveSemaphore.Release();
            }

            return null;
        }

        private BitmapImage CreatePlaceholder(string text, int width = 400, int height = 600)
        {
            try
            {
                var drawingVisual = new System.Windows.Media.DrawingVisual();
                using (var dc = drawingVisual.RenderOpen())
                {
                    dc.DrawRectangle(System.Windows.Media.Brushes.White, null, 
                        new System.Windows.Rect(0, 0, width, height));

                    var formattedText = new System.Windows.Media.FormattedText(
                        text,
                        System.Globalization.CultureInfo.CurrentCulture,
                        System.Windows.FlowDirection.LeftToRight,
                        new System.Windows.Media.Typeface("Segoe UI"),
                        20,
                        System.Windows.Media.Brushes.Gray,
                        1.0);

                    dc.DrawText(formattedText, 
                        new System.Windows.Point((width - formattedText.Width) / 2, 
                                                 (height - formattedText.Height) / 2));
                }

                var renderBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                renderBitmap.Render(drawingVisual);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

                using var ms = new MemoryStream();
                encoder.Save(ms);
                ms.Position = 0;

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze();

                return image;
            }
            catch
            {
                return null;
            }
        }

        // Métodos de carga por formato (simplificados)
        private async Task LoadCBZAsync()
        {
            await Task.Run(() =>
            {
                using var archive = ZipFile.OpenRead(_filePath);
                var entries = archive.Entries
                    .Where(e => !string.IsNullOrEmpty(e.FullName) 
                        && !e.FullName.EndsWith("/")
                        && IsImageExtension(Path.GetExtension(e.FullName)))
                    .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (int i = 0; i < entries.Count; i++)
                {
                    _pages.Add(new ComicPage(i + 1, entries[i].FullName, null));
                }
            });
        }

        private async Task LoadCBRAsync()
        {
            await Task.Run(() =>
            {
                using var archive = RarArchive.Open(_filePath);
                var entries = archive.Entries
                    .Where(e => !e.IsDirectory && IsImageExtension(Path.GetExtension(e.Key)))
                    .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (int i = 0; i < entries.Count; i++)
                {
                    _pages.Add(new ComicPage(i + 1, entries[i].Key, null));
                }
            });
        }

        private async Task LoadCBTAsync()
        {
            await Task.Run(() =>
            {
                using var archive = TarArchive.Open(_filePath);
                var entries = archive.Entries
                    .Where(e => !e.IsDirectory && IsImageExtension(Path.GetExtension(e.Key)))
                    .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (int i = 0; i < entries.Count; i++)
                {
                    _pages.Add(new ComicPage(i + 1, entries[i].Key, null));
                }
            });
        }

        private async Task LoadCB7Async()
        {
            await Task.Run(() =>
            {
                using var archive = SevenZipArchive.Open(_filePath);
                var entries = archive.Entries
                    .Where(e => !e.IsDirectory && IsImageExtension(Path.GetExtension(e.Key)))
                    .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (int i = 0; i < entries.Count; i++)
                {
                    _pages.Add(new ComicPage(i + 1, entries[i].Key, null));
                }
            });
        }

        private async Task LoadFolderAsync(string folderPath)
        {
            await Task.Run(() =>
            {
                var files = Directory.GetFiles(folderPath)
                    .Where(f => IsImageExtension(Path.GetExtension(f)))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (int i = 0; i < files.Count; i++)
                {
                    _pages.Add(new ComicPage(i + 1, files[i], null));
                }
            });
        }

        private Task LoadPDFAsync()
        {
            // TODO: Implementar con Docnet.Core si SUPPORT_PDF está definido
            _pages.Add(new ComicPage(1, "PDF_Page_1", CreatePlaceholder("PDF no soportado aún")));
            return Task.CompletedTask;
        }

        private Task LoadEPUBAsync()
        {
            // TODO: Implementar con VersOne.Epub
            _pages.Add(new ComicPage(1, "EPUB_Page_1", CreatePlaceholder("EPUB no soportado aún")));
            return Task.CompletedTask;
        }

        private bool IsImageExtension(string ext)
        {
            var lower = ext?.ToLowerInvariant();
            return lower == ".jpg" || lower == ".jpeg" || lower == ".png" 
                || lower == ".gif" || lower == ".bmp" || lower == ".webp";
        }

        public void Clear()
        {
            _cts?.Cancel();
            _pages.Clear();
            _cache.Clear();
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cache?.Dispose();
            _decodeSemaphore?.Dispose();
            _archiveSemaphore?.Dispose();
        }
    }
}
