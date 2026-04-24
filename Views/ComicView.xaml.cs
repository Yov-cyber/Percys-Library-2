using System.Windows.Controls;
using System.Windows;
using System.ComponentModel;

namespace ComicReader.Views
{
    public partial class ComicView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        
        public ComicView()
        {
            InitializeComponent();
            DataContext = this;
        }

        // Propiedades para el binding (implementar cuando sea necesario)
        public string CurrentComicTitle => "Sin cómic";
        public string CurrentPageDisplay => "-";
        public double CurrentPageProgress => 0;

    // Referencia a MainWindow para acceder a la lógica
    private ComicReader.MainWindow MainWindowRef => App.Current.MainWindow as ComicReader.MainWindow;

        // Métodos placeholder para los eventos (implementar cuando sea necesario)
        public void PrevPage_Click(object sender, System.Windows.RoutedEventArgs e) { }
        public void NextPage_Click(object sender, System.Windows.RoutedEventArgs e) { }
        public void ZoomIn_Click(object sender, System.Windows.RoutedEventArgs e) { }
        public void ZoomOut_Click(object sender, System.Windows.RoutedEventArgs e) { }
        public void Rotate_Click(object sender, System.Windows.RoutedEventArgs e) { }
        public void ToggleNightMode_Execute(object sender, System.Windows.RoutedEventArgs e) { }
        public void ToggleReadingMode_Execute(object sender, System.Windows.RoutedEventArgs e) { }
        public void AddBookmark_Execute(object sender, System.Windows.RoutedEventArgs e) { }

        // Método para mostrar/ocultar el indicador de carga
        public void ShowLoading(bool show)
        {
            // Usar FindName para evitar advertencias del diseñador cuando no existe el .g.cs en tiempo de diseño
            if (this.FindName("LoadingPanel") is System.Windows.Controls.Grid panel)
            {
                panel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // Método para actualizar la imagen
        public void UpdateImage(System.Windows.Media.ImageSource imageSource)
        {
            // Usar FindName para evitar advertencias del diseñador cuando no existe el .g.cs en tiempo de diseño
            if (this.FindName("imageViewer") is System.Windows.Controls.Image img)
            {
                img.Source = imageSource;
            }
        }

        // Actualizar las propiedades cuando cambie la página
        public void UpdatePageInfo()
        {
            OnPropertyChanged(nameof(CurrentComicTitle));
            OnPropertyChanged(nameof(CurrentPageDisplay));
            OnPropertyChanged(nameof(CurrentPageProgress));
        }

        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
