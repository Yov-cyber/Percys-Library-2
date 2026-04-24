using System;
using System.Threading;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ComicReader.Models;
using ComicReader.Services;
using ComicReader.Core.Abstractions;
using ComicReader.Core.Services;
using ComicReader.Core.Adapters;
using ComicReader.Commands;
using System.Windows.Media.Imaging;

namespace ComicReader.ViewModels
{
    public class ContinuousComicViewModel : INotifyPropertyChanged
    {
    private IComicPageLoader _loader;
    private readonly ILogService _log;
    private readonly IReadingStatsService _stats = ComicReader.Core.Services.ServiceLocator.TryGet<IReadingStatsService>();
    private bool _isLoading;
    private int _currentPage;
    private double _zoom = 1.0;
    private bool _isUserScroll = true; // Para evitar bucles al sincronizar scroll
    private int _overscan = 4;
    private int _releaseMultiplier = 3; // distancia en múltiplos de overscan para liberar
    private DateTime _lastMaterialize = DateTime.MinValue;
    private const int MaterializeThrottleMs = 120; // evitar materializaciones demasiado frecuentes
    // Token para cancelar materializaciones en curso cuando llega una nueva petición
    private CancellationTokenSource _materializeCts = new CancellationTokenSource();
    // Limitar concurrencia de decodificación en la VM para no saturar I/O/CPU con comics grandes
    private readonly SemaphoreSlim _materializeSem = new SemaphoreSlim(Math.Max(1, Math.Min(4, Environment.ProcessorCount)));
    private readonly System.Collections.Concurrent.ConcurrentBag<Task> _backgroundTasks = new System.Collections.Concurrent.ConcurrentBag<Task>();

    public ObservableCollection<ComicPage> Pages { get; } = new();
        public event PropertyChangedEventHandler PropertyChanged;
    public event System.Action<int> CurrentPageChanged;

        public IComicPageLoader Loader
        {
            get => _loader;
            set
            {
                if (_loader != null)
                {
                    // unsubscribe previous
                    try { _loader.FullImageReady -= OnFullImageReady; } catch { }
                }
                _loader = value;
                if (_loader != null)
                {
                    try { _loader.FullImageReady += OnFullImageReady; } catch { }
                    try
                    {
                        // Aplicar ajustes actuales al loader y refrescar tuning (si es la implementación concreta)
                        var win = SettingsManager.Settings?.PrefetchWindow ?? _overscan;
                        if (_loader is ComicReader.Services.ComicPageLoader concrete)
                        {
                            try { concrete.SetPrefetchWindow(win); } catch { }
                            try { concrete.RefreshTuningFromSettings(); } catch { }
                        }
                    }
                    catch { }
                }
                OnPropertyChanged(nameof(Loader));
                try { var t = LoadPagesAsync(); _backgroundTasks.Add(t); } catch { }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set { _isLoading = value; OnPropertyChanged(nameof(IsLoading)); OnPropertyChanged(nameof(IsNotLoading)); }
        }
        public bool IsNotLoading => !IsLoading;

        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (_currentPage != value)
                {
                    _currentPage = value;
                    OnPropertyChanged(nameof(CurrentPage));
                    OnPropertyChanged(nameof(CurrentPageDisplay));
                    OnPropertyChanged(nameof(CurrentPageOneBased));
                    // Actualizar bandera IsCurrent en cada página
                    foreach (var p in Pages)
                        p.IsCurrent = p.PageIndex == _currentPage;
                    CurrentPageChanged?.Invoke(_currentPage);
                    // Materializar entorno al cambiar página
                    RequestVisiblePagesMaterialization();
                    _stats?.RecordPageViewed(_currentPage + 1);
                }
            }
        }

        public int CurrentPageDisplay => _currentPage + 1;

        public double Zoom
        {
            get => _zoom;
            set
            {
                var clamped = Math.Max(0.25, Math.Min(4.0, value));
                if (Math.Abs(_zoom - clamped) > 0.0001)
                {
                    _zoom = clamped;
                    OnPropertyChanged(nameof(Zoom));
                    OnPropertyChanged(nameof(ZoomPercent));
                }
            }
        }

        public int PagesCount => Pages.Count;
        public int ZoomPercent => (int)Math.Round(Zoom * 100);
        public int CurrentPageOneBased
        {
            get => CurrentPage + 1;
            set
            {
                int target = value - 1;
                if (target >= 0 && target < Pages.Count)
                    CurrentPage = target;
            }
        }

    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand ResetZoomCommand { get; }
    public ICommand GoToPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand PrevPageCommand { get; }
    public ICommand MoveLeftCommand { get; }
    public ICommand MoveRightCommand { get; }

        public ContinuousComicViewModel()
        {
            _log = ServiceLocator.TryGet<ILogService>() ?? new LogServiceAdapter();
            ZoomInCommand = new RelayCommand(_ => Zoom += 0.1);
            ZoomOutCommand = new RelayCommand(_ => Zoom -= 0.1);
            ResetZoomCommand = new RelayCommand(_ => Zoom = 1.0);
            GoToPageCommand = new RelayCommand(p =>
            {
                if (p is int i && i >= 0 && i < Pages.Count) CurrentPage = i;
            });
            NextPageCommand = new RelayCommand(_ => { if (CurrentPage + 1 < Pages.Count) CurrentPage++; });
            PrevPageCommand = new RelayCommand(_ => { if (CurrentPage - 1 >= 0) CurrentPage--; });
            // Respetar la dirección de lectura en los botones de navegación del panel
            MoveLeftCommand = new RelayCommand(_ =>
            {
                if (SettingsManager.Settings?.CurrentReadingDirection == ReadingDirection.RightToLeft)
                {
                    if (CurrentPage + 1 < Pages.Count) CurrentPage++;
                }
                else
                {
                    if (CurrentPage - 1 >= 0) CurrentPage--;
                }
            });
            MoveRightCommand = new RelayCommand(_ =>
            {
                if (SettingsManager.Settings?.CurrentReadingDirection == ReadingDirection.RightToLeft)
                {
                    if (CurrentPage - 1 >= 0) CurrentPage--;
                }
                else
                {
                    if (CurrentPage + 1 < Pages.Count) CurrentPage++;
                }
            });

            try
            {
                // Sincronizar overscan con la ventana de prefetch si existe
                if (SettingsManager.Settings != null)
                {
                    _overscan = Math.Max(2, SettingsManager.Settings.PrefetchWindow);
                    // Escuchar cambios en ajustes relevantes
                    SettingsManager.Settings.PropertyChanged += (s, e) =>
                    {
                        try
                        {
                            if (string.Equals(e.PropertyName, nameof(SettingsManager.Settings.PrefetchWindow), StringComparison.OrdinalIgnoreCase))
                            {
                                _overscan = Math.Max(2, SettingsManager.Settings.PrefetchWindow);
                                try { if (_loader is ComicReader.Services.ComicPageLoader c2) c2.SetPrefetchWindow(SettingsManager.Settings.PrefetchWindow); } catch { }
                            }
                        }
                        catch { }
                    };
                }
            }
            catch { }
        }

        private async Task LoadPagesAsync()
        {
            if (Loader == null) { _log?.Log("Loader nulo en ContinuousComicViewModel", LogLevel.Warning); return; }
            try
            {
                IsLoading = true;
                Pages.Clear();
                if (Loader.Pages == null || Loader.Pages.Count == 0)
                {
                    // Solo intentamos cargar si hay una ruta válida; si no, mantenemos el lector vacío
                    if (!string.IsNullOrWhiteSpace(Loader.FilePath))
                        await Loader.LoadComicAsync(Loader.FilePath);
                    else
                    {
                        IsLoading = false;
                        return;
                    }
                }
                int idx = 0;
                foreach (var p in Loader.Pages)
                {
                    // Imagen diferida (virtualización)
                    Pages.Add(new ComicPage { PageNumber = idx + 1, PageIndex = idx, FileName = p.FileName, Image = null, IsCurrent = false });
                    idx++;
                }
                OnPropertyChanged(nameof(PagesCount));
                CurrentPage = 0;
                _log?.Log($"Continuous view cargó {Pages.Count} páginas", LogLevel.Info);
                RequestVisiblePagesMaterialization();
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async void RequestVisiblePagesMaterialization()
        {
            if (Pages.Count == 0 || Loader == null) return;
            // Throttle para evitar ejecuciones muy frecuentes durante scroll rápido
            var now = DateTime.UtcNow;
            if ((now - _lastMaterialize).TotalMilliseconds < MaterializeThrottleMs) return;
            _lastMaterialize = now;
            int center = CurrentPage;
            int start = Math.Max(0, center - _overscan);
            int end = Math.Min(Pages.Count - 1, center + _overscan);
            var indices = Enumerable.Range(start, end - start + 1).ToList();
            // Cancelar materializaciones previas y crear nuevo token
            try { _materializeCts.Cancel(); } catch { }
            _materializeCts = new CancellationTokenSource();
            var token = _materializeCts.Token;
            var tasks = new List<Task>();
            foreach (var i in indices)
            {
                var page = Pages[i];
                if (page.Image != null) continue;
                // lanzar tarea limitada por semáforo para evitar spikes
                try
                {
                    await _materializeSem.WaitAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Cancel requested while waiting for semaphore; skip scheduling this index
                    continue;
                }
                catch { continue; }
                var localI = i;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        if (token.IsCancellationRequested) return;
                        int desiredWidth = Math.Max(600, Math.Min(3000, (int)(1920 * Zoom)));
                        BitmapImage bmp = null;
                        try
                        {
                            if (Loader is ComicReader.Services.ComicPageLoader concreteCl)
                                bmp = await concreteCl.GetPageImageAsync(localI, desiredWidth, token).ConfigureAwait(false);
                            else
                                bmp = await Loader.GetPageImageAsync(localI, desiredWidth).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { return; }

                        if (bmp != null)
                        {
                            // actualizar en UI thread
                            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                try { Pages[localI].Image = bmp; } catch { }
                            }));
                        }
                    }
                    catch (Exception ex)
                    {
                        _log?.Log($"Error materializando p\u00e1gina {localI}: {ex.Message}", LogLevel.Warning);
                    }
                    finally { try { _materializeSem.Release(); } catch { } }
                }, token));
            }
            try { await Task.WhenAll(tasks); } catch { }
            // Liberar páginas lejanas
            int releaseDistance = _overscan * _releaseMultiplier;
            for (int i = 0; i < Pages.Count; i++)
            {
                if (Math.Abs(i - center) > releaseDistance)
                {
                    var page = Pages[i];
                    if (page.Image != null)
                    {
                        page.Image = null; // GC friendly; loader mantiene cache interno
                    }
                }
            }
            // Trigger adicional de prefetch en el loader (por si el loader tiene otra heurística)
            try { if (Loader is ComicReader.Services.ComicPageLoader c) c.PreloadPages(center); } catch { }
        }

        // Nueva sobrecarga: materializar alrededor de un índice central suministrado (útil cuando los containers están virtualizados)
        public async void RequestVisiblePagesMaterialization(int centerIndex)
        {
            if (Pages.Count == 0 || Loader == null) return;
            var now = DateTime.UtcNow;
            if ((now - _lastMaterialize).TotalMilliseconds < MaterializeThrottleMs) return;
            _lastMaterialize = now;
            int center = Math.Max(0, Math.Min(Pages.Count - 1, centerIndex));
            int start = Math.Max(0, center - _overscan);
            int end = Math.Min(Pages.Count - 1, center + _overscan);
            var indices = Enumerable.Range(start, end - start + 1).ToList();
            int maxConcurrent = Math.Max(1, Math.Min(3, Environment.ProcessorCount));
            var sem = new System.Threading.SemaphoreSlim(maxConcurrent);
            // Cancel previous materializations and create a new token to allow cancellation from callers
            try { _materializeCts.Cancel(); } catch { }
            _materializeCts = new CancellationTokenSource();
            var token = _materializeCts.Token;
            var tasks = new List<Task>();
            foreach (var i in indices)
            {
                var page = Pages[i];
                if (page.Image != null) continue;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await sem.WaitAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { return; }
                    try
                    {
                        if (token.IsCancellationRequested) return;
                        int desiredWidth = Math.Max(600, Math.Min(3000, (int)(1920 * Zoom)));
                        BitmapImage bmp = null;
                        try
                        {
                            if (Loader is ComicReader.Services.ComicPageLoader concreteCl)
                                bmp = await concreteCl.GetPageImageAsync(i, desiredWidth, token).ConfigureAwait(false);
                            else
                                bmp = await Loader.GetPageImageAsync(i, desiredWidth).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { return; }

                        if (!token.IsCancellationRequested)
                        {
                            page.Image = bmp;
                        }
                    }
                    catch { }
                    finally { try { sem.Release(); } catch { } }
                }, token));
            }
            try { await Task.WhenAll(tasks); } catch { }
            int releaseDistance = _overscan * _releaseMultiplier;
            for (int i = 0; i < Pages.Count; i++)
            {
                if (Math.Abs(i - center) > releaseDistance)
                {
                    var page = Pages[i];
                    if (page.Image != null) page.Image = null;
                }
            }
            try { if (Loader is ComicReader.Services.ComicPageLoader c) c.PreloadPages(center); } catch { }
        }

        public void BeginProgrammaticScroll() => _isUserScroll = false;
        public void EndProgrammaticScroll() => _isUserScroll = true;

        public void Dispose()
        {
            try { _materializeCts?.Cancel(); } catch { }
            try { Task.WaitAll(_backgroundTasks.ToArray(), 1200); } catch { }
            try { _materializeSem?.Dispose(); } catch { }
        }

        private void OnFullImageReady(int pageIndex, System.Windows.Media.Imaging.BitmapImage fullImage)
        {
            try
            {
                // Asegurar que el índice es válido y actualizar la página correspondiente
                if (pageIndex >= 0 && pageIndex < Pages.Count)
                {
                    // Ejecutar en el hilo de UI
                    System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        try { Pages[pageIndex].Image = fullImage; } catch { }
                    }));
                }
            }
            catch { }
        }

        public bool ShouldReactToUserScroll => _isUserScroll;

        private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
