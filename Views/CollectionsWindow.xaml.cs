using System;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using ComicReader.ViewModels;
using ComicReader.Core.Abstractions;

namespace ComicReader.Views
{
    public partial class CollectionsWindow : Window
    {
        private CollectionsViewModel Vm => DataContext as CollectionsViewModel;

        public CollectionsWindow()
        {
            try
            {
                var mi = this.GetType().GetMethod("InitializeComponent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                mi?.Invoke(this, null);
            }
            catch { }
            // DataContext is set in XAML
            
            // ✅ SUSCRIBIRSE A CAMBIOS DE TEMA
            try
            {
                ComicReader.Themes.ThemeManager.ThemeChanged += OnThemeChanged;
                UpdateThemeResources();
            }
            catch { }
        }
        
        private void OnThemeChanged(ComicReader.Services.ThemeMode mode)
        {
            // Ejecutar en UI thread
            this.Dispatcher?.BeginInvoke(new Action(() =>
            {
                try
                {
                    UpdateThemeResources();
                    this.InvalidateVisual();
                    this.UpdateLayout();
                    ComicReader.Utils.ModernLogger.Info("✓ CollectionsWindow actualizada con nuevo tema");
                }
                catch { }
            }));
        }
        
        private void UpdateThemeResources()
        {
            try
            {
                this.Background = this.TryFindResource("WindowBackgroundBrush") as System.Windows.Media.Brush 
                    ?? this.TryFindResource("CS_Background") as System.Windows.Media.Brush
                    ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(18, 18, 18));
            }
            catch { }
        }

        private void NewCollection_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new NewCollectionDialog() { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                var req = new CollectionCreateRequest { Name = dlg.CollectionName, Description = dlg.Description, CoverPath = dlg.CoverPath };
                Vm?.CreateFromRequest(req);
            }
        }

        private void BtnRename_Click(object sender, RoutedEventArgs e)
        {
            var dto = Vm?.SelectedCollection;
            if (dto == null) return;
            var dlg = new EditCollectionDialog() { Owner = this };
            dlg.CollectionName = dto.Name;
            dlg.Description = dto.Description;
            dlg.SetCoverPath(dto.CoverPath);
            dlg.Items = dto.Items != null ? dto.Items : new System.Collections.Generic.List<Core.Abstractions.ComicItemDto>();
            if (dlg.ShowDialog() == true)
            {
                var req = new Core.Abstractions.CollectionCreateRequest { Name = dlg.CollectionName, Description = dlg.Description, CoverPath = dlg.CoverPath, Items = dlg.Items };
                Vm?.UpdateFromRequest(dto.Id, req);
                ComicReader.Services.ToastService.Show("Colección actualizada.");
            }
        }

        private void BtnDuplicate_Click(object sender, RoutedEventArgs e)
        {
            var dto = Vm?.SelectedCollection;
            if (dto == null) return;
            Vm?.DuplicateCollection(dto);
            ComicReader.Services.ToastService.Show("Colección duplicada.");
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var dto = Vm?.SelectedCollection;
            if (dto == null) return;
            var res = MessageBox.Show(this, $"¿Eliminar la colección '{dto.Name}'? Esta acción se podrá deshacer con 'Deshacer' en el toast.", "Eliminar colección", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Yes)
            {
                Vm?.DeleteCollection(dto);
            }
        }

        private void ImportCollections_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = "JSON Collections|*.json|All Files|*.*";
            if (dlg.ShowDialog(this) == true)
            {
                Vm?.ImportFromFile(dlg.FileName);
                ComicReader.Services.ToastService.Show("Importación completada.");
            }
        }

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            try
            {
                var tb = sender as System.Windows.Controls.TextBox;
                var query = tb?.Text ?? string.Empty;
                var colList = this.FindName("CollectionsListBox") as System.Windows.Controls.ListBox;
                var view = System.Windows.Data.CollectionViewSource.GetDefaultView(colList?.ItemsSource);
                if (view == null) return;
                if (string.IsNullOrWhiteSpace(query))
                {
                    view.Filter = null;
                }
                else
                {
                    view.Filter = o =>
                    {
                        if (o is Core.Abstractions.CollectionDto c)
                        {
                            return (c.Name ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || (c.Description ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                        }
                        return false;
                    };
                }
                view.Refresh();
            }
            catch { }
        }

        private void Favorite_Checked(object sender, RoutedEventArgs e)
        {
            var tb = sender as System.Windows.Controls.Primitives.ToggleButton;
            if (tb == null) return;
            var vm = tb.DataContext as ComicReader.ViewModels.ComicItemViewModel;
            if (vm == null) return;
            // persist change by updating the selected collection DTO list
            var col = Vm?.SelectedCollection;
            if (col == null) return;
            try
            {
                // sync VM state back into DTO list
                var dto = col.Items?.FirstOrDefault(i => string.Equals(i.Path, vm.Path, StringComparison.OrdinalIgnoreCase));
                if (dto != null)
                {
                    dto.IsFavorite = vm.IsFavorite;
                }
                Vm?.UpdateFromRequest(col.Id, new Core.Abstractions.CollectionCreateRequest { Name = col.Name, Description = col.Description, CoverPath = col.CoverPath, Items = col.Items });
            }
            catch { }
        }

        private void Favorite_Unchecked(object sender, RoutedEventArgs e)
        {
            Favorite_Checked(sender, e);
        }

        private void AddCollectionItem_Click(object sender, RoutedEventArgs e)
        {
            var col = Vm?.SelectedCollection;
            if (col == null) return;
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "Cómics|*.cbz;*.cbr;*.zip;*.rar|Todos los archivos|*.*";
            dlg.Multiselect = true;
            if (dlg.ShowDialog(this) == true)
            {
                foreach (var f in dlg.FileNames)
                {
                    var item = new Core.Abstractions.ComicItemDto { Path = f, Title = System.IO.Path.GetFileNameWithoutExtension(f), ThumbPath = string.Empty, IsFavorite = false };
                    col.Items.Add(item);
                    // also add VM to view collection
                    Vm?.SelectedItems.Add(new ComicReader.ViewModels.ComicItemViewModel(item));
                }
                try { Vm?.UpdateFromRequest(col.Id, new Core.Abstractions.CollectionCreateRequest { Name = col.Name, Description = col.Description, CoverPath = col.CoverPath, Items = col.Items }); } catch { }
                // ensure thumbs start generating
                _ = Vm?.GetType().GetMethod("EnsureThumbnailsForSelectedAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(Vm, new object[] { col });
            }
        }

        private void RemoveCollectionItem_Click(object sender, RoutedEventArgs e)
        {
            var col = Vm?.SelectedCollection;
            if (col == null) return;
            var itemsList = this.FindName("ItemsListView") as System.Windows.Controls.ListView;
            if (itemsList == null) return;
            var selectedVMs = itemsList.SelectedItems.Cast<ComicReader.ViewModels.ComicItemViewModel>().ToList();
            if (selectedVMs.Count == 0) return;
            foreach (var s in selectedVMs)
            {
                // remove from VM collection
                Vm?.SelectedItems.Remove(s);
                // remove matching DTO from collection
                var dto = col.Items?.FirstOrDefault(i => string.Equals(i.Path, s.Path, StringComparison.OrdinalIgnoreCase));
                if (dto != null) col.Items.Remove(dto);
            }
            try { Vm?.UpdateFromRequest(col.Id, new Core.Abstractions.CollectionCreateRequest { Name = col.Name, Description = col.Description, CoverPath = col.CoverPath, Items = col.Items }); } catch { }
        }

        private void ShowFavoritesOnly_Checked(object sender, RoutedEventArgs e)
        {
            ApplyFavoritesFilter(true);
        }

        private void ShowFavoritesOnly_Unchecked(object sender, RoutedEventArgs e)
        {
            ApplyFavoritesFilter(false);
        }

        private void ApplyFavoritesFilter(bool onlyFavorites)
        {
            try
            {
                var il = this.FindName("ItemsListView") as System.Windows.Controls.ListView;
                var view = System.Windows.Data.CollectionViewSource.GetDefaultView(il?.ItemsSource);
                if (view == null) return;
                if (onlyFavorites)
                {
                    view.Filter = o =>
                    {
                        if (o is ComicReader.ViewModels.ComicItemViewModel it) return it.IsFavorite;
                        return false;
                    };
                }
                else
                {
                    view.Filter = null;
                }
                view.Refresh();
            }
            catch { }
        }

        private void ExportCollections_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog();
            dlg.Filter = "JSON Collections|*.json|All Files|*.*";
            dlg.FileName = "collections-export.json";
            if (dlg.ShowDialog(this) == true)
            {
                Vm?.ExportToFile(dlg.FileName);
                ComicReader.Services.ToastService.Show("Exportación guardada.");
            }
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Ctrl+I => Import
            if (e.Key == System.Windows.Input.Key.I && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
            {
                ImportCollections_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            // Ctrl+E => Export
            if (e.Key == System.Windows.Input.Key.E && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
            {
                ExportCollections_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            // Delete => delete focused collection
            if (e.Key == System.Windows.Input.Key.Delete)
            {
                var focused = System.Windows.Input.FocusManager.GetFocusedElement(this) as System.Windows.DependencyObject;
                var card = FindParent<Controls.CollectionCard>(focused);
                if (card != null)
                {
                    // call viewmodel delete
                    var dto = card.DataContext as Core.Abstractions.CollectionDto;
                    if (dto != null) Vm?.DeleteCollection(dto);
                    e.Handled = true;
                }
            }

            // Enter => open edit when a CollectionCard is focused
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                var focused = System.Windows.Input.FocusManager.GetFocusedElement(this) as System.Windows.DependencyObject;
                var card = FindParent<Controls.CollectionCard>(focused);
                if (card != null)
                {
                    // invoke edit
                    card.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    e.Handled = true;
                }
            }
        }

        private void OpenFavorites_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Open the MODERN redesigned FavoritesWindow (Material Design 3)
                var win = new FavoritesWindow();
                win.Owner = this;
                // reuse same viewmodel so FavoriteItems binding works and updates in-place
                win.DataContext = this.DataContext;
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                // ERROR HANDLING MODERNO
                ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(
                    ex, 
                    "Error al abrir la ventana de Favoritos", 
                    ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Notify);
            }
        }

        private static T FindParent<T>(System.Windows.DependencyObject child) where T : class
        {
            if (child == null) return null;
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T t) return t;
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}
