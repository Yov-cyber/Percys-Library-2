using System;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ComicReader.ViewModels;
using ComicReader.Services;
using ComicReader.Core.Abstractions;

namespace ComicReader.Views
{
    public partial class ThumbnailGridView : UserControl
    {
        public ViewModels.ThumbnailGridViewViewModel ViewModel { get; }

    public event System.Action<int> PageSelected
        {
            add { ViewModel.PageSelected += value; }
            remove { ViewModel.PageSelected -= value; }
        }
    public event System.Action<int> PageDoubleClicked
        {
            add { ViewModel.PageDoubleClicked += value; }
            remove { ViewModel.PageDoubleClicked -= value; }
        }

        public ThumbnailGridView()
        {
            InitializeComponent();
            ViewModel = new ViewModels.ThumbnailGridViewViewModel();
            DataContext = ViewModel;
        }

            // Animación ligera cuando un item se carga por primera vez
            private void OnThumbnailItemLoaded(object sender, System.Windows.RoutedEventArgs e)
            {
                try
                {
                    if (sender is FrameworkElement fe)
                    {
                        fe.RenderTransform = new TranslateTransform(0, 6);
                        fe.Opacity = 0;
                        var sb = new Storyboard();
                        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                        Storyboard.SetTarget(fade, fe);
                        Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
                        var rise = new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                        Storyboard.SetTarget(rise, fe);
                        Storyboard.SetTargetProperty(rise, new PropertyPath("RenderTransform.(TranslateTransform.Y)"));
                        sb.Children.Add(fade);
                        sb.Children.Add(rise);
                        sb.Begin();
                    }
                }
                catch { }
            }

    public IComicPageLoader ComicLoader { get => ViewModel.ComicLoader; set => ViewModel.ComicLoader = value; }
        public int CurrentPageIndex { get => ViewModel.CurrentPageIndex; set => ViewModel.CurrentPageIndex = value; }
        public int ThumbnailSize { get => ViewModel.ThumbnailSize; set => ViewModel.ThumbnailSize = value; }
        public bool ShowPageNumbers { get => ViewModel.ShowPageNumbers; set => ViewModel.ShowPageNumbers = value; }
        public bool ShowBookmarks { get => ViewModel.ShowBookmarks; set => ViewModel.ShowBookmarks = value; }
        public bool IsLoading => ViewModel.IsLoading;
        public bool IsNotLoading => ViewModel.IsNotLoading;
    }
}
