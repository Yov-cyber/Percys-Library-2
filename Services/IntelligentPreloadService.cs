using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicReader.Core.Abstractions;
using ComicReader.Utils;

namespace ComicReader.Services
{
    /// <summary>
    /// Servicio de pre-carga inteligente que aprende del comportamiento del usuario
    /// y precarga páginas anticipándose a su lectura
    /// </summary>
    public sealed class IntelligentPreloadService
    {
        private static readonly Lazy<IntelligentPreloadService> _instance = 
            new Lazy<IntelligentPreloadService>(() => new IntelligentPreloadService());
        
        public static IntelligentPreloadService Instance => _instance.Value;

        private readonly object _lock = new object();
        private IComicPageLoader _currentLoader;
        private int _currentPage = -1;
        private int _lastDirection = 0; // -1 = atrás, 1 = adelante
        private readonly Queue<DateTime> _pageNavigationTimestamps = new Queue<DateTime>();
        private readonly TimeSpan _timeWindow = TimeSpan.FromSeconds(30);
        private CancellationTokenSource _preloadCts;

        // Configuración adaptativa
        private int _basePreloadCount = 3;
        private int _maxPreloadCount = 8;
        private double _averagePageTimeSeconds = 10.0;

        private IntelligentPreloadService()
        {
            ModernLogger.Info("✓ IntelligentPreloadService inicializado");
        }

        /// <summary>
        /// Configura el loader actual y la página inicial
        /// </summary>
        public void SetupLoader(IComicPageLoader loader, int initialPage)
        {
            lock (_lock)
            {
                _currentLoader = loader;
                _currentPage = initialPage;
                _lastDirection = 1; // Asumir adelante por defecto
                _pageNavigationTimestamps.Clear();
            }
        }

        /// <summary>
        /// Notifica cambio de página y desencadena pre-carga inteligente
        /// </summary>
        public void OnPageChanged(int newPage, int oldPage)
        {
            lock (_lock)
            {
                if (_currentLoader == null) return;

                // Detectar dirección
                int direction = newPage > oldPage ? 1 : -1;
                _lastDirection = direction;
                _currentPage = newPage;

                // Registrar timestamp para calcular velocidad
                var now = DateTime.UtcNow;
                _pageNavigationTimestamps.Enqueue(now);
                
                // Limpiar timestamps antiguos
                while (_pageNavigationTimestamps.Count > 0 && 
                       now - _pageNavigationTimestamps.Peek() > _timeWindow)
                {
                    _pageNavigationTimestamps.Dequeue();
                }

                // Calcular velocidad de lectura
                CalculateReadingSpeed();

                // Cancelar pre-carga anterior
                _preloadCts?.Cancel();
                _preloadCts = new CancellationTokenSource();

                // Iniciar nueva pre-carga
                _ = Task.Run(() => PreloadIntelligentAsync(_preloadCts.Token));
            }
        }

        /// <summary>
        /// Calcula la velocidad de lectura del usuario
        /// </summary>
        private void CalculateReadingSpeed()
        {
            if (_pageNavigationTimestamps.Count < 2) return;

            var timestamps = _pageNavigationTimestamps.ToArray();
            var totalTime = (timestamps[timestamps.Length - 1] - timestamps[0]).TotalSeconds;
            var pageCount = timestamps.Length;

            if (pageCount > 0 && totalTime > 0)
            {
                _averagePageTimeSeconds = totalTime / pageCount;
                
                // Ajustar cantidad de pre-carga basado en velocidad
                if (_averagePageTimeSeconds < 3.0)
                {
                    // Lectura rápida - pre-cargar más
                    _basePreloadCount = Math.Min(_maxPreloadCount, 6);
                }
                else if (_averagePageTimeSeconds < 5.0)
                {
                    // Lectura normal
                    _basePreloadCount = 4;
                }
                else
                {
                    // Lectura lenta - pre-cargar menos
                    _basePreloadCount = 3;
                }

                ModernLogger.Info($"📊 Velocidad de lectura: {_averagePageTimeSeconds:F1}s/página, Pre-carga: {_basePreloadCount}");
            }
        }

        /// <summary>
        /// Pre-carga inteligente de páginas
        /// </summary>
        private async Task PreloadIntelligentAsync(CancellationToken ct)
        {
            try
            {
                if (_currentLoader == null) return;

                int totalPages = _currentLoader.Pages?.Count ?? 0;
                if (totalPages == 0) return;

                var pagesToPreload = new List<int>();

                // ESTRATEGIA 1: Pre-cargar en dirección de lectura
                int forwardCount = (int)Math.Ceiling(_basePreloadCount * 0.7); // 70% adelante
                int backwardCount = _basePreloadCount - forwardCount; // 30% atrás

                // Páginas adelante
                for (int i = 1; i <= forwardCount; i++)
                {
                    int page = _currentPage + (i * _lastDirection);
                    if (page >= 0 && page < totalPages && !pagesToPreload.Contains(page))
                    {
                        pagesToPreload.Add(page);
                    }
                }

                // Páginas atrás (por si el usuario retrocede)
                for (int i = 1; i <= backwardCount; i++)
                {
                    int page = _currentPage - (i * _lastDirection);
                    if (page >= 0 && page < totalPages && !pagesToPreload.Contains(page))
                    {
                        pagesToPreload.Add(page);
                    }
                }

                // ESTRATEGIA 2: Pre-cargar página actual si aún no está
                if (!pagesToPreload.Contains(_currentPage))
                {
                    pagesToPreload.Insert(0, _currentPage);
                }

                // Pre-cargar con prioridad
                ModernLogger.Info($"🔄 Pre-cargando {pagesToPreload.Count} páginas: [{string.Join(", ", pagesToPreload)}]");

                foreach (var pageIndex in pagesToPreload)
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        // Pre-cargar en segundo plano sin bloquear
                        _ = _currentLoader.GetPageImageAsync(pageIndex);
                        
                        // Pequeño delay para no saturar
                        await Task.Delay(50, ct);
                    }
                    catch (Exception ex)
                    {
                        DevLogger.Error($"Error pre-cargando página {pageIndex}: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelación normal
            }
            catch (Exception ex)
            {
                DevLogger.Error($"Error en pre-carga inteligente: {ex.Message}");
            }
        }

        /// <summary>
        /// Obtiene estadísticas del servicio
        /// </summary>
        public string GetStatistics()
        {
            lock (_lock)
            {
                return $@"
IntelligentPreloadService Statistics:
- Current Page: {_currentPage}
- Reading Direction: {(_lastDirection > 0 ? "Forward" : "Backward")}
- Average Page Time: {_averagePageTimeSeconds:F1}s
- Preload Count: {_basePreloadCount}
- Recent Navigations: {_pageNavigationTimestamps.Count}
";
            }
        }

        /// <summary>
        /// Resetea estadísticas
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _preloadCts?.Cancel();
                _pageNavigationTimestamps.Clear();
                _currentPage = -1;
                _lastDirection = 1;
                _basePreloadCount = 3;
                _averagePageTimeSeconds = 10.0;
            }
        }
    }
}
