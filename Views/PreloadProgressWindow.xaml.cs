using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ComicReader.Views
{
    public partial class PreloadProgressWindow : Window
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();
        public CancellationToken Token => _cts.Token;
        private DateTime _startTime = DateTime.MinValue;
        private int _lastReported = 0;

        public PreloadProgressWindow()
        {
            InitializeComponent();
        }

        public void Report(int completed, int total)
        {
            try
            {
                if (_startTime == DateTime.MinValue) _startTime = DateTime.UtcNow;
                double pct = total <= 0 ? 0 : (completed * 100.0 / total);
                var now = DateTime.UtcNow;
                this.Dispatcher.Invoke(() =>
                {
                    var pctClamped = Math.Min(100, Math.Max(0, pct));
                    ProgressBar.Value = pctClamped;
                    ProgressText.Text = $"{completed} / {total}";
                    ProgressPct.Text = $"{Math.Round(pctClamped)}%";
                    // Calculate ETA
                    var elapsed = (now - _startTime).TotalSeconds;
                    if (elapsed >= 0.5 && completed > 0 && total > 0)
                    {
                        var rate = completed / elapsed; // items per second
                        var remaining = Math.Max(0, total - completed);
                        var secs = rate > 0 ? remaining / rate : double.NaN;
                        if (!double.IsNaN(secs) && !double.IsInfinity(secs))
                        {
                            if (secs >= 3600) EtaText.Text = $"ETA: {TimeSpan.FromSeconds(secs):hh\\:mm\\:ss}";
                            else if (secs >= 60) EtaText.Text = $"ETA: {TimeSpan.FromSeconds(secs):mm\\:ss}";
                            else EtaText.Text = $"ETA: {Math.Ceiling(secs)}s";
                        }
                        else EtaText.Text = "";
                    }
                    else
                    {
                        EtaText.Text = "";
                    }
                    if (completed >= total)
                    {
                        CancelButton.IsEnabled = false;
                        CloseButton.IsEnabled = true;
                    }
                    _lastReported = completed;
                });
            }
            catch { }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try { _cts.Cancel(); } catch { }
            CancelButton.IsEnabled = false;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
