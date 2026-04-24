using System.Windows.Controls;
using ComicReader.ViewModels;

namespace ComicReader.Views
{
    public partial class FavoritesSection : UserControl
    {
        public CollectionViewModel ViewModel { get; private set; }

        public FavoritesSection()
        {
            InitializeComponent();

            // Prefer the shared App.FavoritesViewModel if available, otherwise respect provided DataContext or create a local VM.
            var app = System.Windows.Application.Current as App;
            if (app?.FavoritesViewModel != null)
            {
                ViewModel = app.FavoritesViewModel;
                this.DataContext = ViewModel;
            }
            else if (this.DataContext is CollectionViewModel existing)
            {
                ViewModel = existing;
            }
            else
            {
                ViewModel = new CollectionViewModel();
                this.DataContext = ViewModel;
            }
        }
    }
}
