using System.Windows;
using System.Windows.Controls;
using ComicReader.Views;

namespace ComicReader.Views.Controls
{
    public partial class CollectionCard : UserControl
    {
        public CollectionCard()
        {
            try
            {
                var mi = this.GetType().GetMethod("InitializeComponent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                mi?.Invoke(this, null);
            }
            catch { }
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            var win = Window.GetWindow(this);
            var vm = win?.DataContext as ViewModels.CollectionsViewModel;
            var dto = DataContext as Core.Abstractions.CollectionDto;
            if (dto == null || vm == null) return;

            var dlg = new EditCollectionDialog() { Owner = win };
            dlg.CollectionName = dto.Name;
            dlg.Description = dto.Description;
            dlg.CoverPath = dto.CoverPath;
            // Use safe setter instead of relying on generated field
            try { dlg.SetCoverPath(dto.CoverPath); } catch { }
            dlg.Items = dto.Items != null ? dto.Items : new System.Collections.Generic.List<Core.Abstractions.ComicItemDto>();
            if (dlg.ShowDialog() == true)
            {
                var req = new Core.Abstractions.CollectionCreateRequest { Name = dlg.CollectionName, Description = dlg.Description, CoverPath = dlg.CoverPath, Items = dlg.Items };
                vm.UpdateFromRequest(dto.Id, req);
                ComicReader.Services.ToastService.Show("Colección actualizada.");
            }
        }

        private void Duplicate_Click(object sender, RoutedEventArgs e)
        {
            var win = Window.GetWindow(this);
            var vm = win?.DataContext as ViewModels.CollectionsViewModel;
            var dto = DataContext as Core.Abstractions.CollectionDto;
            if (dto == null || vm == null) return;
            vm.DuplicateCollection(dto);
            ComicReader.Services.ToastService.Show("Colección duplicada.");
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var win = Window.GetWindow(this);
            var vm = win?.DataContext as ViewModels.CollectionsViewModel;
            var dto = DataContext as Core.Abstractions.CollectionDto;
            if (dto == null || vm == null) return;

            var res = MessageBox.Show(win, $"¿Eliminar la colección '{dto.Name}'? Esta acción no se puede deshacer.", "Eliminar colección", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Yes)
            {
                vm.DeleteCollection(dto);
            }
        }
    }
}
