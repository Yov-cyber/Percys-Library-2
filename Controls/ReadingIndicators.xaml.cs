using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ComicReader.Utils;

namespace ComicReader.Controls
{
    /// <summary>
    /// Control de indicadores visuales animados para lectura
    /// </summary>
    public partial class ReadingIndicators : UserControl
    {
        private DispatcherTimer _hideTimer;
        private Storyboard _spinnerAnimation;

        public ReadingIndicators()
        {
            InitializeComponent();
            
            _hideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _hideTimer.Tick += HideTimer_Tick;

            InitializeSpinnerAnimation();
        }

        private void InitializeSpinnerAnimation()
        {
            _spinnerAnimation = new Storyboard
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            var rotationAnimation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(1)
            };

            Storyboard.SetTarget(rotationAnimation, SpinnerRotation);
            Storyboard.SetTargetProperty(rotationAnimation, 
                new PropertyPath(RotateTransform.AngleProperty));

            _spinnerAnimation.Children.Add(rotationAnimation);
        }

        // ============================================================
        // PAGE INDICATOR
        // ============================================================

        public void ShowPageIndicator(int currentPage, int totalPages)
        {
            PageText.Text = $"Página {currentPage} de {totalPages}";
            
            // Actualizar barra de progreso
            var progressPercent = totalPages > 0 ? (double)currentPage / totalPages : 0;
            ProgressBar.Width = 100 * progressPercent;

            ShowIndicator(PageIndicator);
            ModernLogger.Info($"📄 Indicador de página: {currentPage}/{totalPages}");
        }

        // ============================================================
        // ZOOM INDICATOR
        // ============================================================

        public void ShowZoomIndicator(double zoomPercent)
        {
            ZoomText.Text = $"{zoomPercent:F0}%";
            ShowIndicator(ZoomIndicator);
            ModernLogger.Info($"🔍 Indicador de zoom: {zoomPercent:F0}%");
        }

        // ============================================================
        // LOADING INDICATOR
        // ============================================================

        public void ShowLoadingIndicator(string message = "Cargando...")
        {
            LoadingText.Text = message;
            LoadingIndicator.Opacity = 0;
            LoadingIndicator.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(200)
            };
            LoadingIndicator.BeginAnimation(OpacityProperty, fadeIn);

            _spinnerAnimation.Begin();
            ModernLogger.Info($"⏳ Indicador de carga: {message}");
        }

        public void HideLoadingIndicator()
        {
            _spinnerAnimation.Stop();

            var fadeOut = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(200)
            };
            fadeOut.Completed += (s, e) =>
            {
                LoadingIndicator.Visibility = Visibility.Collapsed;
            };
            LoadingIndicator.BeginAnimation(OpacityProperty, fadeOut);
        }

        // ============================================================
        // BOOKMARK INDICATOR
        // ============================================================

        public void ShowBookmarkIndicator(bool added = true)
        {
            BookmarkText.Text = added ? "Marcador agregado" : "Marcador eliminado";
            ShowIndicatorWithScale(BookmarkIndicator);
        }

        // ============================================================
        // PANEL INDICATOR
        // ============================================================

        public void ShowPanelIndicator(int currentPanel, int totalPanels)
        {
            PanelText.Text = $"{currentPanel}/{totalPanels}";

            // Actualizar dots
            PanelDots.Children.Clear();
            for (int i = 0; i < totalPanels; i++)
            {
                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = i == currentPanel - 1 ? 
                        new SolidColorBrush(Colors.White) : 
                        new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                    Margin = new Thickness(2)
                };
                PanelDots.Children.Add(dot);
            }

            ShowIndicator(PanelIndicator);
        }

        // ============================================================
        // ERROR INDICATOR
        // ============================================================

        public void ShowErrorIndicator(string errorMessage)
        {
            ErrorText.Text = errorMessage;
            ShowIndicatorWithScale(ErrorIndicator, hideAfter: 4000);
            ModernLogger.Error($"❌ Indicador de error: {errorMessage}");
        }

        // ============================================================
        // GENERIC SHOW/HIDE
        // ============================================================

        private void ShowIndicator(UIElement indicator, int hideAfterMs = 2000)
        {
            indicator.Opacity = 0;
            indicator.Visibility = Visibility.Visible;

            var fadeIn = (Storyboard)Resources["FadeInAnimation"];
            fadeIn = fadeIn.Clone();
            Storyboard.SetTarget(fadeIn, indicator);
            fadeIn.Begin();

            _hideTimer.Stop();
            _hideTimer.Interval = TimeSpan.FromMilliseconds(hideAfterMs);
            _hideTimer.Tag = indicator;
            _hideTimer.Start();
        }

        private void ShowIndicatorWithScale(UIElement indicator, int hideAfter = 2000)
        {
            indicator.Opacity = 0;
            indicator.Visibility = Visibility.Visible;

            // Fade in
            var fadeIn = new DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(300)
            };

            // Scale up
            var scaleAnimation = (Storyboard)Resources["ScaleUpAnimation"];
            scaleAnimation = scaleAnimation.Clone();
            Storyboard.SetTarget(scaleAnimation, indicator);

            indicator.BeginAnimation(OpacityProperty, fadeIn);
            scaleAnimation.Begin();

            _hideTimer.Stop();
            _hideTimer.Interval = TimeSpan.FromMilliseconds(hideAfter);
            _hideTimer.Tag = indicator;
            _hideTimer.Start();
        }

        private void HideTimer_Tick(object sender, EventArgs e)
        {
            _hideTimer.Stop();
            
            if (_hideTimer.Tag is UIElement indicator)
            {
                var fadeOut = (Storyboard)Resources["FadeOutAnimation"];
                fadeOut = fadeOut.Clone();
                Storyboard.SetTarget(fadeOut, indicator);
                fadeOut.Completed += (s, args) =>
                {
                    indicator.Visibility = Visibility.Collapsed;
                };
                fadeOut.Begin();
            }
        }

        // ============================================================
        // MANUAL HIDE
        // ============================================================

        public void HideAllIndicators()
        {
            _hideTimer.Stop();
            HideLoadingIndicator();

            foreach (var child in new[] { PageIndicator, ZoomIndicator, 
                BookmarkIndicator, PanelIndicator, ErrorIndicator })
            {
                child.Visibility = Visibility.Collapsed;
                child.Opacity = 0;
            }
        }
    }
}
