using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace ComicReader.Views.Controls
{
    public partial class ProgressComicItem : UserControl
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, BitmapImage> _imageCache = new System.Collections.Concurrent.ConcurrentDictionary<string, BitmapImage>();

        public ProgressComicItem()
        {
            try
            {
                var mi = this.GetType().GetMethod("InitializeComponent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                mi?.Invoke(this, null);
            }
            catch { }
            this.Loaded += ProgressComicItem_Loaded;
        }

        private void ProgressComicItem_Loaded(object sender, RoutedEventArgs e)
        {
            // Lazy-load thumbnail if a path is provided
            if (!string.IsNullOrEmpty(ThumbnailPath))
            {
                var bmp = _imageCache.GetOrAdd(ThumbnailPath, path =>
                {
                    try
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
                        bi.DecodePixelWidth = 200; // optimize size
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.EndInit();
                        bi.Freeze();
                        return bi;
                    }
                    catch
                    {
                        return null;
                    }
                });

                try { var img = this.FindName("ThumbnailImage") as System.Windows.Controls.Image; if (bmp != null && img != null) img.Source = bmp; }
                catch { }
            }

            // placeholder visibility and fade-in for thumbnail
                try
                {
                    bool hasSource = false;
                    try { var img = this.FindName("ThumbnailImage") as System.Windows.Controls.Image; hasSource = img?.Source != null; } catch { }
                    var ph = this.FindName("PlaceholderIcon") as System.Windows.FrameworkElement;
                    if (ph != null) ph.Visibility = hasSource ? Visibility.Collapsed : Visibility.Visible;
                    if (hasSource && SystemParameters.ClientAreaAnimation)
                    {
                        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240));
                        try { var img2 = this.FindName("ThumbnailImage") as System.Windows.Controls.Image; img2?.BeginAnimation(OpacityProperty, fade); } catch { }
                    }
                }
                catch { }

            // Fade in if animations are enabled
                try
                {
                    if (SystemParameters.ClientAreaAnimation)
                    {
                        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
                        this.BeginAnimation(OpacityProperty, anim);
                    }
                    else
                    {
                        this.Opacity = 1;
                    }
                }
                catch { this.Opacity = 1; }

            // Accessibility: set AutomationProperties for screen readers
            try
            {
                var title = Title ?? string.Empty;
                var pages = PagesText ?? string.Empty;
                var name = string.IsNullOrWhiteSpace(title) ? (pages) : ($"{title}. {pages}");
                System.Windows.Automation.AutomationProperties.SetName(this, name);
                System.Windows.Automation.AutomationProperties.SetHelpText(this, AccessibilityHelp ?? name);
            }
            catch { }
        }

        public static readonly DependencyProperty ThumbnailPathProperty = DependencyProperty.Register(
            nameof(ThumbnailPath), typeof(string), typeof(ProgressComicItem), new PropertyMetadata(string.Empty, OnThumbnailPathChanged));

        public static readonly DependencyProperty PagesTextProperty = DependencyProperty.Register(
            nameof(PagesText), typeof(string), typeof(ProgressComicItem), new PropertyMetadata(string.Empty, OnPagesTextChanged));

        public static readonly DependencyProperty AccessibilityHelpProperty = DependencyProperty.Register(
            nameof(AccessibilityHelp), typeof(string), typeof(ProgressComicItem), new PropertyMetadata(string.Empty));

        private static void OnThumbnailPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ProgressComicItem ctrl)
            {
                ctrl.LoadThumbnailAsync(e.NewValue as string);
            }
        }

        public string ThumbnailPath
        {
            get => (string)GetValue(ThumbnailPathProperty);
            set => SetValue(ThumbnailPathProperty, value);
        }

        public string PagesText
        {
            get => (string)GetValue(PagesTextProperty);
            set => SetValue(PagesTextProperty, value);
        }

        public string AccessibilityHelp
        {
            get => (string)GetValue(AccessibilityHelpProperty);
            set => SetValue(AccessibilityHelpProperty, value);
        }

        private static void OnPagesTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ProgressComicItem ctrl)
            {
                ctrl.UpdateAccessibilityHelp();
            }
        }

        private async void LoadThumbnailAsync(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    // clear image
                    try { var img = this.FindName("ThumbnailImage") as System.Windows.Controls.Image; if (img != null) img.Source = null; } catch { }
                    try { var ph = this.FindName("PlaceholderIcon") as System.Windows.FrameworkElement; if (ph != null) ph.Visibility = Visibility.Visible; } catch { }
                    return;
                }

                // load on threadpool to avoid UI freeze
                BitmapImage bmp = null;
                await Task.Run(() =>
                {
                    try
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
                        bi.DecodePixelWidth = 200;
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.EndInit();
                        bi.Freeze();
                        bmp = bi;
                    }
                    catch { bmp = null; }
                }).ConfigureAwait(false);

                // assign on UI thread and animate row (slide + fade) when thumbnail appears
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var img = this.FindName("ThumbnailImage") as System.Windows.Controls.Image;
                        var ph = this.FindName("PlaceholderIcon") as System.Windows.FrameworkElement;
                        var root = this.FindName("Root") as System.Windows.FrameworkElement;

                        if (img != null) img.Source = bmp;
                        if (ph != null) ph.Visibility = bmp == null ? Visibility.Visible : Visibility.Collapsed;

                        if (bmp != null && SystemParameters.ClientAreaAnimation)
                        {
                            // make sure Root has a TranslateTransform
                            try
                            {
                                if (root != null)
                                {
                                    if (!(root.RenderTransform is TranslateTransform))
                                    {
                                        root.RenderTransform = new TranslateTransform(6, 0);
                                    }

                                    var tt = root.RenderTransform as TranslateTransform;

                                    // start from slightly below and transparent
                                    if (tt != null) tt.Y = 6;
                                    root.Opacity = 0;
                                    if (img != null) img.Opacity = 0;

                                    var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                                    var slide = new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

                                    root.BeginAnimation(UIElement.OpacityProperty, fade);
                                    tt?.BeginAnimation(TranslateTransform.YProperty, slide);
                                    img?.BeginAnimation(UIElement.OpacityProperty, fade);
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }

        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(ProgressComicItem), new PropertyMetadata(string.Empty, OnTitleChanged));

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ProgressComicItem ctrl)
            {
                ctrl.UpdateAccessibilityHelp();
            }
        }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly DependencyProperty PercentageProperty = DependencyProperty.Register(
            nameof(Percentage), typeof(double), typeof(ProgressComicItem), new PropertyMetadata(0.0, OnPercentageChanged));

        private static void OnPercentageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ProgressComicItem ctrl)
            {
                try
                {
                    var newVal = (double)e.NewValue;
                    try
                    {
                        var pb = ctrl.FindName("InnerProgress") as ProgressBar;
                        if (pb != null)
                        {
                            if (SystemParameters.ClientAreaAnimation)
                            {
                                var from = pb.Value;
                                var da = new DoubleAnimation(from, newVal, TimeSpan.FromMilliseconds(350)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                                pb.BeginAnimation(ProgressBar.ValueProperty, da);
                            }
                            else
                            {
                                pb.Value = newVal;
                            }

                            // color the progress bar based on thresholds
                            try
                            {
                                var brush = (Brush)Application.Current.FindResource("RS_AccentBrush");
                                if (newVal >= 80) brush = (Brush)Application.Current.FindResource("RS_SuccessBrush");
                                else if (newVal >= 40) brush = (Brush)Application.Current.FindResource("RS_WarningBrush");
                                else brush = (Brush)Application.Current.FindResource("RS_DangerBrush");
                                pb.Foreground = brush;
                            }
                            catch { }
                        }

                        // Update the accessibility help text when percentage changes
                        try { ctrl.UpdateAccessibilityHelp(); } catch { }
                    }
                    catch
                    {
                        try
                        {
                            var pb2 = ctrl.FindName("InnerProgress") as ProgressBar;
                            if (pb2 != null) pb2.Value = newVal;
                        }
                        catch { }
                    }
                }
                catch { try { var pb = ctrl.FindName("InnerProgress") as ProgressBar; if (pb != null) pb.Value = (double)e.NewValue; } catch { } }
            }
        }

        public double Percentage
        {
            get => (double)GetValue(PercentageProperty);
            set => SetValue(PercentageProperty, value);
        }

        private void UpdateAccessibilityHelp()
        {
            try
            {
                var pct = Percentage;
                var pages = PagesText ?? string.Empty;
                var title = Title ?? string.Empty;
                var help = $"{title}. {pages}. {pct:F0} por ciento leído.";
                AccessibilityHelp = help;
                try
                {
                    var name = string.IsNullOrWhiteSpace(title) ? pages : ($"{title}. {pages}");
                    System.Windows.Automation.AutomationProperties.SetName(this, name);
                    System.Windows.Automation.AutomationProperties.SetHelpText(this, help);
                }
                catch { }
            }
            catch { }
        }
    }
}
