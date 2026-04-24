using System.Windows;
using System.Windows.Input;

namespace ComicReader
{
    public partial class GoToPageDialog : Window
    {
        public int SelectedPage { get; private set; }
        private int _maxPages;

        public GoToPageDialog(int maxPages, int currentPage)
        {
            InitializeComponent();
            _maxPages = maxPages;
            PageNumberTextBox.Text = currentPage.ToString();
            PageRangeLabel.Content = $"(1 - {maxPages})";
            PageNumberTextBox.SelectAll();
            PageNumberTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(PageNumberTextBox.Text, out int pageNumber))
            {
                if (pageNumber >= 1 && pageNumber <= _maxPages)
                {
                    SelectedPage = pageNumber;
                    DialogResult = true;
                    Close();
                }
                else
                {
                    ComicReader.Services.Notifications.NotificationService.Instance.Warning($"Por favor ingresa un número entre 1 y {_maxPages}", "Página inválida");
                    PageNumberTextBox.SelectAll();
                    PageNumberTextBox.Focus();
                }
            }
            else
            {
                ComicReader.Services.Notifications.NotificationService.Instance.Warning("Por favor ingresa un número válido", "Entrada inválida");
                PageNumberTextBox.SelectAll();
                PageNumberTextBox.Focus();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void PageNumberTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OkButton_Click(sender, e);
            }
            else if (e.Key == Key.Escape)
            {
                CancelButton_Click(sender, e);
            }
        }

        private void PageNumberTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Solo permitir números
            e.Handled = !IsTextNumeric(e.Text);
        }

        private static bool IsTextNumeric(string text)
        {
            return int.TryParse(text, out _);
        }
    }
}