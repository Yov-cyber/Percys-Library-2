using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Reflection;
using ComicReader.Models;
using ComicReader.Services;

namespace ComicReader.Views
{
    public partial class ComicSearchWindow : Window
    {
        private readonly ObservableCollection<ContinueItem> _results = new ObservableCollection<ContinueItem>();
        private readonly ContinueReadingService _svc = ContinueReadingService.Instance;

        public ComicSearchWindow()
        {
            InitializeComponent();
            var resultsList = this.FindName("ResultsList") as System.Windows.Controls.ListView;
            if (resultsList != null) resultsList.ItemsSource = _results;
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e) => DoSearch();

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            var sb = this.FindName("SearchBox") as System.Windows.Controls.TextBox;
            var ff = this.FindName("FolderFilter") as System.Windows.Controls.TextBox;
            if (sb != null) sb.Text = string.Empty;
            if (ff != null) ff.Text = string.Empty;
            _results.Clear();
        }

        // XAML uses ClearSearch_Click in the modern template
        private void ClearSearch_Click(object sender, RoutedEventArgs e) => ClearButton_Click(sender, e);

        private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();

        private void ResultsList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) OpenSelected();
        }

        private void OpenSelected_Click(object sender, RoutedEventArgs e) => OpenSelected();

        private void OpenResult_Click(object sender, RoutedEventArgs e) => OpenSelected();

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void DoSearch()
        {
            _results.Clear();
            var sb2 = this.FindName("SearchBox") as System.Windows.Controls.TextBox;
            var ff2 = this.FindName("FolderFilter") as System.Windows.Controls.TextBox;
            var fo = this.FindName("FilterOngoing") as System.Windows.Controls.CheckBox;
            var fc = this.FindName("FilterCompleted") as System.Windows.Controls.CheckBox;
            var q = (sb2?.Text ?? string.Empty).Trim();
            var folder = (ff2?.Text ?? string.Empty).Trim();
            bool includeOngoing = fo?.IsChecked == true;
            bool includeCompleted = fc?.IsChecked == true;

            try
            {
                var items = _svc.Items.Concat(_svc.CompletedItems ?? Enumerable.Empty<ContinueItem>());
                var filtered = items.Where(it =>
                    (string.IsNullOrEmpty(q) || (it.DisplayName ?? string.Empty).IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)
                );
                if (!string.IsNullOrEmpty(folder)) filtered = filtered.Where(it => it.FilePath != null && it.FilePath.IndexOf(folder, StringComparison.CurrentCultureIgnoreCase) >= 0);
                if (!includeOngoing) filtered = filtered.Where(it => it.IsCompleted);
                if (!includeCompleted) filtered = filtered.Where(it => !it.IsCompleted);
                foreach (var r in filtered.Take(500)) _results.Add(r);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error buscando cómics: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenSelected()
        {
            var rl = this.FindName("ResultsList") as System.Windows.Controls.ListView;
            if (rl?.SelectedItem is ContinueItem ci && !string.IsNullOrWhiteSpace(ci.FilePath))
            {
                // Intentar invocar el método OpenComicFile mediante reflexión si es privado
                var main = Application.Current?.MainWindow;
                if (main != null)
                {
                    try
                    {
                        var mi = main.GetType().GetMethod("OpenComicFile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (mi != null)
                        {
                            mi.Invoke(main, new object[] { ci.FilePath });
                            Close();
                            return;
                        }
                    }
                    catch { /* continue to shell open */ }
                }

                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = ci.FilePath, UseShellExecute = true });
                    Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "No se pudo abrir el cómic: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
