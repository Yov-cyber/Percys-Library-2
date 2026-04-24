using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using ComicReader.Core.Abstractions;

namespace ComicReader.Services
{
    /// <summary>
    /// Servicio para pre-cargar todas las páginas de un cómic en segundo plano
    /// para eliminar las imágenes borrosas en modo de lectura uno a uno.
    /// </summary>
    public class PagePreloadService
    {
        private static PagePreloadService _instance;
        public static PagePreloadService Instance => _instance ??= new PagePreloadService();

        private CancellationTokenSource _cts;
        private Task _preloadTask;
        private IComicPageLoader _currentLoader;
        private readonly HashSet<int> _loadedPages = new();
        private int _totalPages = 0;
        private int _progress = 0;

        public event Action<int, int> ProgressChanged; // (loaded, total)
        public event Action PreloadCompleted;

        public int Progress => _progress;
        public int TotalPages => _totalPages;
        public bool IsPreloading => _preloadTask != null && !_preloadTask.IsCompleted;

        private PagePreloadService() { }

        /// <summary>
        /// Inicia la pre-carga de todas las páginas del cómic actual.
        /// </summary>
        public void StartPreload(IComicPageLoader loader)
        {
            if (loader == null || loader.Pages == null || loader.Pages.Count == 0)
            {
                ComicReader.Utils.ModernLogger.Warning("⚠ PagePreloadService: No se puede iniciar pre-carga sin páginas");
                return;
            }

            // Cancelar cualquier pre-carga anterior
            Cancel();

            _currentLoader = loader;
            _totalPages = loader.Pages.Count;
            _progress = 0;
            _loadedPages.Clear();
            _cts = new CancellationTokenSource();

            ComicReader.Utils.ModernLogger.Info($"🚀 Iniciando pre-carga de {_totalPages} páginas");

            _preloadTask = Task.Run(async () => await PreloadAllPagesAsync(_cts.Token), _cts.Token);
        }

        private async Task PreloadAllPagesAsync(CancellationToken ct)
        {
            try
            {
                // Estrategia: cargar primero las páginas cercanas al inicio (1-10),
                // luego el resto en orden secuencial
                var priorityPages = Enumerable.Range(0, Math.Min(10, _totalPages)).ToList();
                var remainingPages = Enumerable.Range(10, Math.Max(0, _totalPages - 10)).ToList();

                // Cargar páginas prioritarias primero (sin paralelismo para evitar sobrecarga)
                foreach (var pageIndex in priorityPages)
                {
                    if (ct.IsCancellationRequested) break;
                    await LoadPageSafely(pageIndex, ct);
                }

                // Cargar resto de páginas con paralelismo limitado (4 a la vez)
                var semaphore = new SemaphoreSlim(4, 4);
                var tasks = remainingPages.Select(async pageIndex =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        if (!ct.IsCancellationRequested)
                            await LoadPageSafely(pageIndex, ct);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToList();

                await Task.WhenAll(tasks);

                if (!ct.IsCancellationRequested)
                {
                    ComicReader.Utils.ModernLogger.Info($"✅ Pre-carga completada: {_progress}/{_totalPages} páginas");
                    PreloadCompleted?.Invoke();
                }
                else
                {
                    ComicReader.Utils.ModernLogger.Warning($"⚠ Pre-carga cancelada: {_progress}/{_totalPages} páginas cargadas");
                }
            }
            catch (OperationCanceledException)
            {
                ComicReader.Utils.ModernLogger.Warning("⚠ Pre-carga cancelada por el usuario");
            }
            catch (Exception ex)
            {
                ComicReader.Utils.ModernLogger.Error($"❌ Error en pre-carga: {ex.Message}");
            }
        }

        private async Task LoadPageSafely(int pageIndex, CancellationToken ct)
        {
            try
            {
                if (_loadedPages.Contains(pageIndex)) return; // Ya está en caché

                // Usar el método existente del loader que maneja el cache internamente
                var image = await _currentLoader.GetPageImageAsync(pageIndex);
                
                if (image != null && !ct.IsCancellationRequested)
                {
                    _loadedPages.Add(pageIndex);
                    _progress = _loadedPages.Count;
                    ProgressChanged?.Invoke(_progress, _totalPages);

                    // Log cada 10 páginas para no saturar el log
                    if (_progress % 10 == 0)
                    {
                        ComicReader.Utils.ModernLogger.Debug($"📖 Pre-carga: {_progress}/{_totalPages} páginas ({(_progress * 100 / _totalPages)}%)");
                    }
                }
            }
            catch (Exception ex)
            {
                ComicReader.Utils.ModernLogger.Error($"❌ Error cargando página {pageIndex}: {ex.Message}");
            }
        }

        /// <summary>
        /// Cancela la pre-carga actual.
        /// </summary>
        public void Cancel()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            _preloadTask = null;
            _loadedPages.Clear();
            _progress = 0;
            _totalPages = 0;
        }

        /// <summary>
        /// Verifica si una página específica ya está pre-cargada.
        /// </summary>
        public bool IsPageLoaded(int pageIndex)
        {
            return _loadedPages.Contains(pageIndex);
        }
    }
}
