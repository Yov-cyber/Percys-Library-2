using System;
using System.Collections.Generic;
using System.Windows;
using System.Threading;
using ComicReader.Core.Services;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ComicReader.Models;

namespace ComicReader
{
    public partial class ThumbnailPanelWindow : Window
    {
        public event Action<int> PageSelected;
        private List<ComicPage> _pages;
        private int _currentPageIndex;
        private ComicReader.Services.ComicPageLoader _loader;
        private CancellationTokenSource _windowCts = new CancellationTokenSource();
        private Task _loadTask;

        public List<ComicPage> Pages => _pages;

        public ThumbnailPanelWindow(List<ComicPage> pages, int currentPageIndex, ComicReader.Services.ComicPageLoader loader = null)
        {
            InitializeComponent();
            _pages = pages ?? new List<ComicPage>();
            _currentPageIndex = currentPageIndex;
            _loader = loader ?? ServiceLocator.TryGet<ComicReader.Services.ComicPageLoader>();
            this.DataContext = this;
            this.Loaded += ThumbnailPanelWindow_Loaded;
            this.Closed += ThumbnailPanelWindow_Closed;
            // Start the thumbnail loading task but keep a reference so we can wait on close
            _loadTask = LoadThumbnailsAsync(_windowCts.Token);
        }

        private void ThumbnailPanelWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Register a single preview mouse handler to detect clicks on items
            var itemsCtrl = this.FindName("ThumbnailsItemsControl") as ItemsControl;
            if (itemsCtrl != null) itemsCtrl.PreviewMouseLeftButtonUp += ThumbnailsItemsControl_PreviewMouseLeftButtonUp;
            ScrollToCurrentPage();
        }

        private void ThumbnailsItemsControl_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Walk up the visual tree to find the DataContext of the clicked item
            DependencyObject src = e.OriginalSource as DependencyObject;
            while (src != null)
            {
                if (src is FrameworkElement fe && fe.DataContext is ComicPage cp)
                {
                    int idx = _pages.IndexOf(cp);
                    if (idx >= 0)
                    {
                        PageSelected?.Invoke(idx);
                        this.Close();
                        e.Handled = true;
                        return;
                    }
                }
                src = VisualTreeHelper.GetParent(src);
            }
        }

        private async Task LoadThumbnailsAsync(CancellationToken token)
        {
            if (_pages == null || _pages.Count == 0) return;
            try
            {
                for (int i = 0; i < _pages.Count; i++)
                {
                    if (token.IsCancellationRequested) break;
                    var page = _pages[i];
                    if (page.Thumbnail != null) continue;
                    try
                    {
                        if (_loader != null)
                        {
                            var thumb = await _loader.GetPageThumbnailAsync(i, 300, 0, token).ConfigureAwait(false);
                            if (token.IsCancellationRequested) break;
                            Application.Current.Dispatcher.Invoke(() => { page.Thumbnail = thumb; });
                        }
                        else if (page.Image != null)
                        {
                            Application.Current.Dispatcher.Invoke(() => page.Thumbnail = page.Image);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void ScrollToCurrentPage()
        {
            if (_currentPageIndex >= 0 && _currentPageIndex < _pages.Count)
            {
                int columns = 4;
                int row = _currentPageIndex / columns;
                
                // Calcular posición de scroll
                double scrollPosition = (double)row / Math.Ceiling((double)_pages.Count / columns);
                var sv = this.FindName("ThumbnailScrollViewer") as ScrollViewer;
                if (sv != null) sv.ScrollToVerticalOffset(scrollPosition * sv.ScrollableHeight);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void ThumbnailPanelWindow_Closed(object sender, EventArgs e)
        {
            try
            {
                _windowCts?.Cancel();
            }
            catch { }

            // Wait a short, bounded time for the thumbnail loading to finish after cancellation
            try
            {
                var t = _loadTask ?? Task.CompletedTask;
                await Task.WhenAny(t, Task.Delay(3000)).ConfigureAwait(false);
            }
            catch { }

            try { var it = this.FindName("ThumbnailsItemsControl") as ItemsControl; if (it != null) it.PreviewMouseLeftButtonUp -= ThumbnailsItemsControl_PreviewMouseLeftButtonUp; } catch { }
            try { this.Loaded -= ThumbnailPanelWindow_Loaded; } catch { }
            try { this.Closed -= ThumbnailPanelWindow_Closed; } catch { }
            try { _windowCts?.Dispose(); } catch { }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }
    }
}