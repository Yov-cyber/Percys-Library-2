// FileName: Views/EnhancedContinuousComicView.xaml.cs
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicReader.Core.Abstractions;
using ComicReader.Models;

namespace ComicReader.Views
{
    /// <summary>
    /// Vista mejorada de lectura continua con:
    /// - Carga instantánea de todas las páginas
    /// - Virtualización inteligente (solo renderiza visibles)
    /// - Zoom con rueda/gestos
    /// - Scroll suave y responsive
    /// </summary>
    public partial class EnhancedContinuousComicView : UserControl
    {
        private IComicPageLoader _loader;
        private ScrollViewer _scrollViewer;
        private ItemsControl _pagesContainer;
        private ScaleTransform _scaleTransform;
        private TranslateTransform _translateTransform;
        
        private double _currentZoom = 1.0;
        private const double MinZoom = 0.5;
        private const double MaxZoom = 5.0;
        private const double ZoomStep = 0.2;
        
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _isLoading = false;

        public ObservableCollection<ComicPageViewModel> Pages { get; } = new ObservableCollection<ComicPageViewModel>();
        
        public event Action<int> CurrentPageChanged;

        public EnhancedContinuousComicView()
        {
            InitializeComponent();
            
            // Configurar transformaciones para zoom
            _scaleTransform = new ScaleTransform(1.0, 1.0);
            _translateTransform = new TranslateTransform();
            
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(_scaleTransform);
            transformGroup.Children.Add(_translateTransform);
            
            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _scrollViewer = this.FindName("MainScrollViewer") as ScrollViewer;
            _pagesContainer = this.FindName("PagesContainer") as ItemsControl;
            
            if (_pagesContainer != null)
            {
                _pagesContainer.RenderTransform = new TransformGroup
                {
                    Children = new TransformCollection { _scaleTransform, _translateTransform }
                };
                _pagesContainer.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            // Configurar eventos de zoom
            if (_scrollViewer != null)
            {
                _scrollViewer.PreviewMouseWheel += OnMouseWheel;
                _scrollViewer.ScrollChanged += OnScrollChanged;
            }

            this.PreviewKeyDown += OnKeyDown;
        }

        public async Task LoadComicAsync(IComicPageLoader loader)
        {
            if (_isLoading) return;
            _isLoading = true;

            try
            {
                _loader = loader;
                Pages.Clear();
                
                // Cancelar operaciones previas
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                // Mostrar indicador de carga
                ShowLoadingIndicator(true);

                // Pre-crear todas las páginas (virtualización se encarga del renderizado)
                for (int i = 0; i < loader.Pages.Count; i++)
                {
                    Pages.Add(new ComicPageViewModel
                    {
                        PageNumber = i,
                        Loader = loader,
                        IsVisible = false // Se actualizará con virtualización
                    });
                }

                // Precarga inteligente: cargar primeras 3 páginas inmediatamente
                await PreloadInitialPages();

                // Iniciar precarga en segundo plano del resto
                _ = PreloadAllPagesBackground();

                ShowLoadingIndicator(false);
            }
            catch (Exception ex)
            {
                ComicReader.Services.Logger.LogException("Error loading comic in continuous view", ex);
                ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(ex, "Cargar cómic en vista continua", ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Notify);
            }
            finally
            {
                _isLoading = false;
            }
        }

        private async Task PreloadInitialPages()
        {
            // Cargar las primeras 3 páginas para mostrar contenido inmediatamente
            var tasks = Enumerable.Range(0, Math.Min(3, Pages.Count))
                .Select(async i =>
                {
                    try
                    {
                        await Pages[i].LoadImageAsync();
                    }
                    catch { }
                });

            await Task.WhenAll(tasks);
        }

        private async Task PreloadAllPagesBackground()
        {
            // Precarga progresiva: empezar desde la página 4 hacia adelante
            for (int i = 3; i < Pages.Count; i++)
            {
                if (_cts.Token.IsCancellationRequested) break;

                try
                {
                    await Pages[i].LoadImageAsync();
                    
                    // Pequeña pausa para no saturar
                    await Task.Delay(50, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ComicReader.Services.Logger.LogException($"Error preloading page {i}", ex);
                }
            }
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Si Ctrl está presionado, hacer zoom
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                
                double zoomDelta = e.Delta > 0 ? ZoomStep : -ZoomStep;
                double newZoom = Math.Max(MinZoom, Math.Min(MaxZoom, _currentZoom + zoomDelta));
                
                if (Math.Abs(newZoom - _currentZoom) > 0.01)
                {
                    ApplyZoom(newZoom);
                }
            }
            // Si no, dejar que ScrollViewer maneje el scroll normal
        }

        private void ApplyZoom(double newZoom)
        {
            _currentZoom = newZoom;
            
            // Animación suave de zoom
            var scaleAnimation = new DoubleAnimation
            {
                To = newZoom,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            _scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            _scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);

            // Notificar cambio de zoom
            UpdateZoomLabel();
        }

        public void ZoomIn()
        {
            double newZoom = Math.Min(MaxZoom, _currentZoom + ZoomStep);
            ApplyZoom(newZoom);
        }

        public void ZoomOut()
        {
            double newZoom = Math.Max(MinZoom, _currentZoom - ZoomStep);
            ApplyZoom(newZoom);
        }

        public void ResetZoom()
        {
            ApplyZoom(1.0);
        }

        private void UpdateZoomLabel()
        {
            var zoomLabel = this.FindName("ZoomLabel") as TextBlock;
            if (zoomLabel != null)
            {
                zoomLabel.Text = $"{Math.Round(_currentZoom * 100)}%";
            }
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // Detectar página actual basada en scroll position
            if (_scrollViewer == null || Pages.Count == 0) return;

            try
            {
                double viewportHeight = _scrollViewer.ViewportHeight;
                double scrollOffset = _scrollViewer.VerticalOffset;
                
                // Calcular página visible central
                int visiblePage = 0;
                double accumulatedHeight = 0;
                
                for (int i = 0; i < Pages.Count; i++)
                {
                    var container = _pagesContainer.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                    if (container != null)
                    {
                        double pageHeight = container.ActualHeight;
                        if (accumulatedHeight + pageHeight / 2 >= scrollOffset)
                        {
                            visiblePage = i;
                            break;
                        }
                        accumulatedHeight += pageHeight + 10; // 10 = margin
                    }
                }

                CurrentPageChanged?.Invoke(visiblePage);

                // Actualizar virtualización: marcar páginas visibles
                UpdateVisibility();
            }
            catch { }
        }

        private void UpdateVisibility()
        {
            if (_scrollViewer == null || _pagesContainer == null) return;

            try
            {
                double viewportTop = _scrollViewer.VerticalOffset;
                double viewportBottom = viewportTop + _scrollViewer.ViewportHeight;
                double buffer = _scrollViewer.ViewportHeight; // Buffer de 1 viewport arriba/abajo

                double accumulatedHeight = 0;

                for (int i = 0; i < Pages.Count; i++)
                {
                    var container = _pagesContainer.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                    if (container != null)
                    {
                        double pageHeight = container.ActualHeight;
                        double pageTop = accumulatedHeight;
                        double pageBottom = pageTop + pageHeight;

                        bool isInViewport = pageBottom >= (viewportTop - buffer) && pageTop <= (viewportBottom + buffer);
                        Pages[i].IsVisible = isInViewport;

                        accumulatedHeight += pageHeight + 10;
                    }
                }
            }
            catch { }
        }

        public void ScrollToPage(int pageNumber)
        {
            if (pageNumber < 0 || pageNumber >= Pages.Count) return;

            try
            {
                var container = _pagesContainer.ItemContainerGenerator.ContainerFromIndex(pageNumber) as FrameworkElement;
                if (container != null && _scrollViewer != null)
                {
                    var transform = container.TransformToAncestor(_pagesContainer);
                    var position = transform.Transform(new Point(0, 0));
                    
                    _scrollViewer.ScrollToVerticalOffset(position.Y);
                }
            }
            catch { }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Add:
                case Key.OemPlus when Keyboard.Modifiers == ModifierKeys.Control:
                    ZoomIn();
                    e.Handled = true;
                    break;
                    
                case Key.Subtract:
                case Key.OemMinus when Keyboard.Modifiers == ModifierKeys.Control:
                    ZoomOut();
                    e.Handled = true;
                    break;
                    
                case Key.D0 when Keyboard.Modifiers == ModifierKeys.Control:
                    ResetZoom();
                    e.Handled = true;
                    break;
            }
        }

        private void ShowLoadingIndicator(bool show)
        {
            var loadingPanel = this.FindName("LoadingPanel") as FrameworkElement;
            if (loadingPanel != null)
            {
                loadingPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // Event handlers para botones de zoom
        private void ZoomInButton_Click(object sender, RoutedEventArgs e) => ZoomIn();
        private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => ZoomOut();
        private void ResetZoomButton_Click(object sender, RoutedEventArgs e) => ResetZoom();

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }

    /// <summary>
    /// ViewModel para cada página en el modo continuo
    /// </summary>
    public class ComicPageViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private System.Windows.Media.Imaging.BitmapImage _image;
        private bool _isLoaded;
        private bool _isVisible;

        public int PageNumber { get; set; }
        public IComicPageLoader Loader { get; set; }

        public System.Windows.Media.Imaging.BitmapImage Image
        {
            get => _image;
            set
            {
                _image = value;
                OnPropertyChanged(nameof(Image));
            }
        }

        public bool IsLoaded
        {
            get => _isLoaded;
            set
            {
                _isLoaded = value;
                OnPropertyChanged(nameof(IsLoaded));
            }
        }

        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible != value)
                {
                    _isVisible = value;
                    OnPropertyChanged(nameof(IsVisible));
                    
                    // Cargar imagen cuando se hace visible
                    if (value && !IsLoaded)
                    {
                        _ = LoadImageAsync();
                    }
                }
            }
        }

        public async Task LoadImageAsync()
        {
            if (IsLoaded || Loader == null) return;

            try
            {
                var image = await Loader.GetPageImageAsync(PageNumber);
                Image = image;
                IsLoaded = true;
            }
            catch (Exception ex)
            {
                ComicReader.Services.Logger.LogException($"Error loading page {PageNumber}", ex);
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    // Métodos públicos adicionales para integración con MainWindow
    public partial class EnhancedContinuousComicView
    {
        /// <summary>
        /// Desplaza una página completa arriba o abajo
        /// </summary>
        public bool ScrollOnePage(bool down)
        {
            if (_scrollViewer == null) return false;

            try
            {
                var offset = down ? _scrollViewer.ViewportHeight : -_scrollViewer.ViewportHeight;
                _scrollViewer.ScrollToVerticalOffset(_scrollViewer.VerticalOffset + offset);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Método placeholder para compatibilidad - EnhancedContinuousComicView no necesita
        /// re-aplicar brillo/contraste ya que las imágenes se cargan con calidad completa
        /// </summary>
        public void ReapplyBrightnessContrastVisible()
        {
            // No-op: las imágenes ya están optimizadas en el loader
        }
    }
}
