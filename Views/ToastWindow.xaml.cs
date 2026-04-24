using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Threading.Tasks;

namespace ComicReader.Views
{
    public partial class ToastWindow : Window
    {
        public enum ToastKind { Info, Success, Warning, Error, Comic }
        private Action _action;
        private System.Windows.Threading.DispatcherTimer _timer;
        private int _durationMs = 2200;
        private int _elapsedMs = 0;
        private const int TICK_MS = 50;

        public ToastWindow()
        {
            try
            {
                var mi = this.GetType().GetMethod("InitializeComponent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                mi?.Invoke(this, null);
            }
            catch { }
            Loaded += ToastWindow_Loaded;
        }

        private void ToastWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // position bottom-right of the primary screen's work area
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 16;
            Top = wa.Bottom - Height - 16;
            // entrance: slide up + fade in
            try
            {
                var rootBorder = this.FindName("RootBorder") as System.Windows.Controls.Border;
                if (rootBorder != null) rootBorder.RenderTransform = new TranslateTransform(0, 14);
            }
            catch { }
            var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            this.BeginAnimation(OpacityProperty, fadeIn);
            try
            {
                var rootBorder = this.FindName("RootBorder") as System.Windows.Controls.Border;
                var tr = (rootBorder?.RenderTransform as TranslateTransform) ?? new TranslateTransform(0, 14);
                var slide = new DoubleAnimation(14, 0, new Duration(TimeSpan.FromMilliseconds(260))) { EasingFunction = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut } };
                if (rootBorder != null)
                {
                    rootBorder.RenderTransform = tr;
                    tr.BeginAnimation(TranslateTransform.YProperty, slide);
                }
            }
            catch { }

            // start TTL timer to update progress and close when elapsed
            _elapsedMs = 0;
            try
            {
                var pf = this.FindName("ProgressFill") as System.Windows.FrameworkElement;
                if (pf != null) pf.Width = 0;
            }
            catch { }

            _timer = new System.Windows.Threading.DispatcherTimer(System.TimeSpan.FromMilliseconds(TICK_MS), System.Windows.Threading.DispatcherPriority.Background, (s, a) =>
            {
                _elapsedMs += TICK_MS;
                try
                {
                    double fraction = Math.Min(1.0, (double)_elapsedMs / Math.Max(1, _durationMs));
                    var totalWidth = (this.ActualWidth - 24); // approximate inner width (padding)
                    try
                    {
                        var pf = this.FindName("ProgressFill") as System.Windows.FrameworkElement;
                        if (pf != null) pf.Width = Math.Max(0, totalWidth * fraction);
                    }
                    catch { }
                }
                catch { }

                if (_elapsedMs >= _durationMs)
                {
                    // stop and fade out
                    _timer.Stop();
                    var fadeOut = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(300)));
                    fadeOut.Completed += (ss, aa) => this.Close();
                    this.BeginAnimation(OpacityProperty, fadeOut);
                }
            }, System.Windows.Threading.Dispatcher.CurrentDispatcher);
            _timer.Start();
        }

        public static void ShowToast(string message)
        {
            ShowToast(message, null, null, 2200, ToastKind.Comic);
        }

        public static void ShowToast(string message, string actionLabel, Action action, int durationMs)
        {
            ShowToast(message, actionLabel, action, durationMs, ToastKind.Comic);
        }

        public static void ShowToast(string message, string actionLabel, Action action, int durationMs, ToastKind kind)
        {
            var w = new ToastWindow();
            // Ensure components and set values via FindName to avoid reliance on generated fields
            try
            {
                var mi = w.GetType().GetMethod("InitializeComponent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                mi?.Invoke(w, null);
            }
            catch { }
            try
            {
                var mt = w.FindName("MessageText") as System.Windows.Controls.TextBlock;
                if (mt != null) mt.Text = message;
            }
            catch { }
            // set visual style based on kind
            try
            {
                var icon = w.FindName("IconText") as System.Windows.Controls.TextBlock;
                var root = w.FindName("RootBorder") as System.Windows.Controls.Border;
                var actionBtn = w.FindName("ActionButton") as System.Windows.Controls.Button;
                switch (kind)
                {
                    case ToastKind.Success:
                        if (icon != null) icon.Text = "✅";
                        if (root != null) root.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FFECF9F0");
                        if (actionBtn != null) actionBtn.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF2BC67D");
                        break;
                    case ToastKind.Warning:
                        if (icon != null) icon.Text = "⚠️";
                        if (root != null) root.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FFFFF7E0");
                        break;
                    case ToastKind.Error:
                        if (icon != null) icon.Text = "❌";
                        if (root != null) root.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FFFFEBEB");
                        break;
                    default:
                        if (icon != null) icon.Text = "💬";
                        if (root != null) root.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF1B1F23");
                        break;
                }
            }
            catch { }
            w._action = action;
            w._durationMs = durationMs <= 0 ? 2200 : durationMs;
            if (!string.IsNullOrWhiteSpace(actionLabel) && action != null)
            {
                try
                {
                    var ab = w.FindName("ActionButton") as System.Windows.Controls.Button;
                    if (ab != null)
                    {
                        ab.Content = actionLabel;
                        ab.Visibility = Visibility.Visible;
                    }
                }
                catch { }
            }
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            var desktop = SystemParameters.WorkArea;
            w.Left = desktop.Right - w.Width - 20;
            w.Top = desktop.Bottom - w.Height - 20;
            w.Show();
        }

        /// <summary>
        /// Async variant that completes when the toast window is closed. Useful for queuing.
        /// </summary>
        public static System.Threading.Tasks.Task ShowToastAsync(string message, string actionLabel, Action action, int durationMs, ToastKind kind)
        {
            var tcs = new System.Threading.Tasks.TaskCompletionSource<object>();
            try
            {
                var w = new ToastWindow();
                try
                {
                    var mi = w.GetType().GetMethod("InitializeComponent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    mi?.Invoke(w, null);
                }
                catch { }
                try { var mt = w.FindName("MessageText") as System.Windows.Controls.TextBlock; if (mt != null) mt.Text = message; } catch { }
                try
                {
                    var icon = w.FindName("IconText") as System.Windows.Controls.TextBlock;
                    var root = w.FindName("RootBorder") as System.Windows.Controls.Border;
                    var actionBtn = w.FindName("ActionButton") as System.Windows.Controls.Button;
                    switch (kind)
                    {
                        case ToastKind.Success:
                            if (icon != null) icon.Text = "✅";
                            if (root != null) root.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FFECF9F0");
                            if (actionBtn != null) actionBtn.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF2BC67D");
                            break;
                        case ToastKind.Warning:
                            if (icon != null) icon.Text = "⚠️";
                            if (root != null) root.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FFFFF7E0");
                            break;
                        case ToastKind.Error:
                            if (icon != null) icon.Text = "❌";
                            if (root != null) root.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FFFFEBEB");
                            break;
                        default:
                            if (icon != null) icon.Text = "💬";
                            if (root != null) root.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF1B1F23");
                            break;
                    }
                }
                catch { }

                w._action = action;
                w._durationMs = durationMs <= 0 ? 2200 : durationMs;
                if (!string.IsNullOrWhiteSpace(actionLabel) && action != null)
                {
                    try
                    {
                        var ab = w.FindName("ActionButton") as System.Windows.Controls.Button;
                        if (ab != null)
                        {
                            ab.Content = actionLabel;
                            ab.Visibility = Visibility.Visible;
                        }
                    }
                    catch { }
                }

                w.WindowStartupLocation = WindowStartupLocation.Manual;
                var desktop = SystemParameters.WorkArea;
                w.Left = desktop.Right - w.Width - 20;
                w.Top = desktop.Bottom - w.Height - 20;

                // when closed, complete tcs
                w.Closed += (s, e) =>
                {
                    try { tcs.TrySetResult(null); } catch { tcs.TrySetResult(null); }
                };

                // show on UI thread
                try { w.Show(); } catch { try { System.Windows.Application.Current.Dispatcher.Invoke(() => w.Show()); } catch { } }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }

        private void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            try { _action?.Invoke(); } catch { }
            this.Close();
        }
    }
}
