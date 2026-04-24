using System.Windows;
using Microsoft.Win32;

namespace ComicReader.Views
{
    public partial class NewCollectionDialog : Window
    {
        public string CollectionName
        {
            get { try { var tb = this.FindName("NameBox") as System.Windows.Controls.TextBox; return tb?.Text?.Trim() ?? string.Empty; } catch { return string.Empty; } }
        }
        public string Description
        {
            get { try { var tb = this.FindName("DescBox") as System.Windows.Controls.TextBox; return tb?.Text?.Trim() ?? string.Empty; } catch { return string.Empty; } }
        }
        public string CoverPath { get; private set; }

        public NewCollectionDialog()
        {
            // Call InitializeComponent if the generated partial exists (use reflection to avoid hard dependency in design/analysis)
            try
            {
                var m = this.GetType().GetMethod("InitializeComponent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                m?.Invoke(this, null);
            }
            catch { }
        }

        private void SelectCover_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp|Todos los archivos|*.*";
            if (dlg.ShowDialog(this) == true)
            {
                CoverPath = dlg.FileName;
                try { var tb = this.FindName("CoverPathText") as System.Windows.Controls.TextBox; if (tb != null) tb.Text = CoverPath; } catch { }
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CollectionName))
            {
                ComicReader.Services.Notifications.NotificationService.Instance.Warning("Debes indicar un nombre para la colección", "Nombre requerido");
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}
