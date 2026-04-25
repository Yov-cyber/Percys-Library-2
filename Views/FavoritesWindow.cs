using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ComicReader.Models;
using ComicReader.Services;

namespace ComicReader.Views
{
    public partial class FavoritesWindow : Window
    {
        public ObservableCollection<ComicCollection> Collections { get; set; }
        public ObservableCollection<FavoriteComic> CurrentCollectionItems { get; set; }
        public ObservableCollection<FavoriteComic> FilteredItems { get; set; }
        private ComicCollection _selectedCollection;

        public FavoritesWindow()
        {
            #pragma warning disable
            try
            {
                var mi = this.GetType().GetMethod("InitializeComponent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                mi?.Invoke(this, null);
            }
            catch (Exception ex)
            {
                // ERROR HANDLING MODERNO - Error crítico en InitializeComponent
                ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(
                    ex, 
                    "Error crítico al inicializar FavoritesWindow", 
                    ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Notify);
            }
            #pragma warning restore

            // Prefer the shared CollectionViewModel exposed by App so multiple views share state.
            var app = System.Windows.Application.Current as App;
            if (app?.FavoritesViewModel != null)
            {
                Collections = app.FavoritesViewModel.Collections;
                // Use shared VM as DataContext so commands and bindings are available
                this.DataContext = app.FavoritesViewModel;
            }
            else
            {
                Collections = FavoritesStorage.Load();
            }
            CurrentCollectionItems = new ObservableCollection<FavoriteComic>();
            FilteredItems = new ObservableCollection<FavoriteComic>();

            GetCollectionsListBox().ItemsSource = Collections;
            GetFavoritesListBox().ItemsSource = FilteredItems;

            // Permitir arrastrar y soltar archivos de comic directamente a la lista.
            // GongSolutions DragDrop (configurado en XAML para el reorder interno)
            // intercepta los eventos preview y marcaria los nativos como handled,
            // dejando los handlers DragOver/Drop nativos sin disparar. Por eso
            // ruteamos todo a traves de un IDropTarget custom: si el data es
            // FileDrop ejecuta la logica nativa, sino delega al DefaultDropHandler
            // (reorder).
            var favList = GetFavoritesListBox();
            favList.AllowDrop = true;
            GongSolutions.Wpf.DragDrop.DragDrop.SetDropHandler(favList,
                new FavoritesDropHandler(
                    onFilesDropped: AddDroppedFilesToCurrentCollection,
                    canAcceptFileDrop: () => _selectedCollection != null,
                    onInternalReorderCommitted: SyncModelOrderFromFiltered));

            // Actualizar estadísticas iniciales
            UpdateStatistics();
        }

        // Guardar automáticamente cuando se cierre la ventana
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            FavoritesStorage.Save(Collections);
        }

        private void CollectionsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var list = GetCollectionsListBox();
            if (list.SelectedItem is ComicCollection collection)
            {
                _selectedCollection = collection;
                SetCollectionTitle($"{collection.Name} ({collection.ItemCount} cómics)");
                
                CurrentCollectionItems.Clear();
                foreach (var item in collection.Items)
                {
                    CurrentCollectionItems.Add(item);
                    
                    // 🎨 Cargar portada si no está cargada
                    if (item.ThumbnailImage == null)
                    {
                        _ = LoadComicCoverAsync(item);
                    }
                }
                
                // Populate tag filter combobox with distinct tags from the collection
                try
                {
                    var combo = GetTagFilterComboBox();
                    if (combo != null)
                    {
                        combo.Items.Clear();
                        combo.Items.Add("(Todos)");
                        var distinct = CurrentCollectionItems.SelectMany(i => i.Tags ?? System.Linq.Enumerable.Empty<string>())
                                                           .Where(t => !string.IsNullOrWhiteSpace(t))
                                                           .Select(t => t.Trim())
                                                           .Distinct(StringComparer.OrdinalIgnoreCase)
                                                           .OrderBy(t => t);
                        foreach (var t in distinct) combo.Items.Add(t);
                        combo.SelectedIndex = 0;
                    }
                }
                catch (Exception ex)
                {
                    // ERROR HANDLING MODERNO - Error no crítico en carga de tags
                    System.Diagnostics.Debug.WriteLine($"Error loading tags: {ex.Message}");
                }

                ApplyFilter();
            }
            else
            {
                _selectedCollection = null;
                SetCollectionTitle("Selecciona una colección");
                CurrentCollectionItems.Clear();
                ApplyFilter();
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void TagFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            FilteredItems.Clear();
            var searchText = GetSearchTextBox().Text?.ToLower() ?? "";
            // Consider tag filter if present
            var tagFilter = (FindName("TagFilterComboBox") as System.Windows.Controls.ComboBox)?.SelectedItem as string;
            if (tagFilter == "(Todos)") tagFilter = null;
            tagFilter = tagFilter?.ToLower();

            var filteredComics = CurrentCollectionItems.Where(c =>
            {
                if (string.IsNullOrEmpty(searchText)) return true;
                var title = c.Title ?? string.Empty;
                var author = c.Author ?? string.Empty;
                System.Collections.Generic.IEnumerable<string> tags = c.Tags ?? System.Linq.Enumerable.Empty<string>();
                var matchesText = title.ToLower().Contains(searchText)
                                  || author.ToLower().Contains(searchText)
                                  || tags.Any(tag => (tag ?? string.Empty).ToLower().Contains(searchText));
                var matchesTag = true;
                if (!string.IsNullOrWhiteSpace(tagFilter))
                {
                    matchesTag = tags.Any(tag => string.Equals(tag?.Trim(), tagFilter, StringComparison.OrdinalIgnoreCase));
                }
                return matchesText && matchesTag;
            });

            foreach (var comic in filteredComics)
            {
                FilteredItems.Add(comic);
            }
        }

        private void CreateCollection_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CreateCollectionDialog();
            dialog.Owner = this;
            
            if (dialog.ShowDialog() == true)
            {
                var newCollection = new ComicCollection
                {
                    Id = Guid.NewGuid(),
                    Name = dialog.CollectionName,
                    Description = dialog.CollectionDescription,
                    DateCreated = DateTime.Now,
                    Color = dialog.SelectedColor
                };
                
                Collections.Add(newCollection);
                GetCollectionsListBox().SelectedItem = newCollection;
                FavoritesStorage.Save(Collections);
            }
        }

        private async void AddToCollection_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCollection == null)
            {
                ComicReader.Services.Notifications.NotificationService.Instance.Info(
                    "Selecciona una colección primero", 
                    "Colección requerida");
                return;
            }

            var openDialog = new OpenFileDialog
            {
                Title = "Agregar Cómic a Colección",
                Filter = "Archivos de Comic|*.cbz;*.cbr;*.cb7;*.cbt;*.zip;*.rar;*.7z;*.tar;*.pdf;*.epub;*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp;*.heic;*.tif;*.tiff;*.avif|Todos los archivos|*.*",
                Multiselect = true
            };

            if (openDialog.ShowDialog() == true)
            {
                int addedCount = 0;
                var coverTasks = new System.Collections.Generic.List<Task>();
                foreach (var filename in openDialog.FileNames)
                {
                    // VALIDACIÓN MODERNA con ValidationService
                    var validation = ComicReader.Services.Validation.ValidationService.Instance.ValidateComicFile(filename);
                    if (!validation.IsValid)
                    {
                        ComicReader.Services.Notifications.NotificationService.Instance.Warning(
                            $"{Path.GetFileName(filename)}: {validation.ErrorMessage}", 
                            "Archivo inválido");
                        continue;
                    }

                    var comic = new FavoriteComic
                    {
                        Id = Guid.NewGuid(),
                        Title = Path.GetFileNameWithoutExtension(filename),
                        FilePath = filename,
                        DateAdded = DateTime.Now,
                        Tags = new System.Collections.ObjectModel.ObservableCollection<string>(new[] { "Sin categorizar" })
                    };

                    _selectedCollection.Items.Add(comic);
                    CurrentCollectionItems.Add(comic);
                    addedCount++;
                    
                    // CARGAR PORTADA ASYNC en background (estilo dinámico)
                    coverTasks.Add(LoadComicCoverAsync(comic));
                }
                
                ApplyFilter();
                UpdateStatistics();
                FavoritesStorage.Save(Collections);
                
                // NOTIFICACIÓN MODERNA premium
                ComicReader.Services.Notifications.NotificationService.Instance.Success(
                    $"{addedCount} cómic(s) agregado(s) a '{_selectedCollection.Name}'", 
                    "Cómics agregados");

                await Task.WhenAll(coverTasks);
            }
        }

        private void AddFolderToCollection_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCollection == null)
            {
                ComicReader.Services.Notifications.NotificationService.Instance.Info(
                    "Selecciona una colección primero", 
                    "Colección requerida");
                return;
            }
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "Seleccionar carpeta con cómics";
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var exts = new[] { ".cbz", ".cbr", ".cb7", ".cbt", ".zip", ".rar", ".7z", ".tar", ".pdf", ".epub",
                                        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff", ".avif" };
                    var files = Directory.GetFiles(dlg.SelectedPath, "*", SearchOption.AllDirectories)
                                          .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                          .Take(300) // límite razonable para evitar bloqueos
                                          .ToList();
                    foreach (var filename in files)
                    {
                        var comic = new FavoriteComic
                        {
                            Id = Guid.NewGuid(),
                            Title = Path.GetFileNameWithoutExtension(filename),
                            FilePath = filename,
                            DateAdded = DateTime.Now,
                            Tags = new System.Collections.ObjectModel.ObservableCollection<string>(new[] { "Sin categorizar" })
                        };
                        _selectedCollection.Items.Add(comic);
                        CurrentCollectionItems.Add(comic);
                    }
                    ApplyFilter();
                    UpdateStatistics();
                    FavoritesStorage.Save(Collections);
                    
                    // NOTIFICACIÓN MODERNA premium
                    ComicReader.Services.Notifications.NotificationService.Instance.Success(
                        $"{files.Count} cómic(s) agregado(s) desde la carpeta", 
                        "Carpeta importada");
                }
            }
        }

        private void OpenFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is FavoriteComic comic)
            {
                OpenComic(comic);
            }
        }

        // MÉTODO CENTRALIZADO para abrir cómics (desde click, double-click, enter, etc)
        private void OpenComic(FavoriteComic comic)
        {
            if (comic == null) return;
            
            if (File.Exists(comic.FilePath))
            {
                try
                {
                    // Actualizar última lectura
                    comic.LastRead = DateTime.Now;
                    FavoritesStorage.Save(Collections);
                    
                    // Intentar abrir el cómic en la ventana principal
                    if (Owner is global::ComicReader.MainWindow mainWindow)
                    {
                        var method = mainWindow.GetType().GetMethod("OpenComicFile", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        method?.Invoke(mainWindow, new object[] { comic.FilePath });
                        mainWindow.ShowComicView();
                        Close();
                    }
                }
                catch (Exception ex)
                {
                    ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(
                        ex, 
                        "Error al abrir el cómic", 
                        ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Notify);
                }
            }
            else
            {
                // NOTIFICACIÓN MODERNA premium con error handling
                ComicReader.Services.Notifications.NotificationService.Instance.Error(
                    $"El archivo '{Path.GetFileName(comic.FilePath)}' no existe o ha sido movido", 
                    "Archivo no encontrado");
            }
        }

        private void RemoveFromCollection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is FavoriteComic comic)
            {
                var result = MessageBox.Show(
                    $"¿Quitar '{comic.Title}' de la colección?",
                    "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // capture for undo
                    var col = _selected_collection_safe();
                    var idx = col?.Items.IndexOf(comic) ?? -1;

                    col?.Items.Remove(comic);
                    CurrentCollectionItems.Remove(comic);
                    FilteredItems.Remove(comic);
                    SetCollectionTitle(_selectedCollection != null 
                        ? $"{_selectedCollection.Name} ({_selectedCollection.ItemCount} cómics)" 
                        : "Selecciona una colección");
                    FavoritesStorage.Save(Collections);

                    // Use central IUndoService so UI and tests can handle undo uniformly
                    try
                    {
                        var undoSvc = ComicReader.Core.Services.ServiceLocator.TryGet<ComicReader.Services.IUndoService>();
                        if (undoSvc != null)
                        {
                            undoSvc.Register($"'{comic.Title}' eliminado", "Deshacer", () =>
                            {
                                try
                                {
                                    if (col != null)
                                    {
                                        if (idx >= 0 && idx <= col.Items.Count)
                                            col.Items.Insert(idx, comic);
                                        else
                                            col.Items.Add(comic);
                                    }
                                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                                    {
                                        CurrentCollectionItems.Add(comic);
                                        if (!FilteredItems.Contains(comic)) FilteredItems.Add(comic);
                                        SetCollectionTitle(col != null ? $"{col.Name} ({col.ItemCount} cómics)" : "Selecciona una colección");
                                    });
                                    FavoritesStorage.Save(Collections);
                                }
                                catch (Exception ex)
                                {
                                    // ERROR HANDLING MODERNO - Falló el undo
                                    System.Diagnostics.Debug.WriteLine($"Undo failed: {ex.Message}");
                                }
                            });
                        }
                        else
                        {
                            // Fallback: use centralized helper which will try IUndoService then show a toast
                            ComicReader.Services.ToastService.ShowWithUndo($"'{comic.Title}' eliminado", "Deshacer", () =>
                            {
                                try
                                {
                                    if (col != null)
                                    {
                                        if (idx >= 0 && idx <= col.Items.Count)
                                            col.Items.Insert(idx, comic);
                                        else
                                            col.Items.Add(comic);
                                    }
                                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                                    {
                                        CurrentCollectionItems.Add(comic);
                                        if (!FilteredItems.Contains(comic)) FilteredItems.Add(comic);
                                        SetCollectionTitle(col != null ? $"{col.Name} ({col.ItemCount} cómics)" : "Selecciona una colección");
                                    });
                                    FavoritesStorage.Save(Collections);
                                }
                                catch (Exception ex)
                                {
                                    // ERROR HANDLING MODERNO - Falló el undo
                                    System.Diagnostics.Debug.WriteLine($"Undo failed: {ex.Message}");
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        // ERROR HANDLING MODERNO - Error general en RemoveFromCollection
                        System.Diagnostics.Debug.WriteLine($"Error removing from collection: {ex.Message}");
                    }
                }
            }
        }

        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCollection == null) return;
            var list = GetFavoritesListBox();
            var selected = list.SelectedItems.Cast<FavoriteComic>().ToList();
            if (selected.Count == 0) return;
            
            // TODO: Reemplazar con diálogo de confirmación moderno (CustomDialog)
            if (MessageBox.Show($"¿Quitar {selected.Count} elemento(s) de la colección?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                // capture removed items and their original indices for undo
                var col = _selected_collection_safe();
                var removed = selected.Select(it => new { Item = it, Index = col?.Items.IndexOf(it) ?? -1 }).ToList();

                foreach (var r in removed)
                {
                    if (r.Item != null)
                    {
                        col?.Items.Remove(r.Item);
                        CurrentCollectionItems.Remove(r.Item);
                        FilteredItems.Remove(r.Item);
                    }
                }

                SetCollectionTitle(_selectedCollection != null
                    ? $"{_selectedCollection.Name} ({_selectedCollection.ItemCount} cómics)"
                    : "Selecciona una colección");
                FavoritesStorage.Save(Collections);

                // Use central IUndoService for bulk undo as well
                try
                {
                    var undoSvc = ComicReader.Core.Services.ServiceLocator.TryGet<ComicReader.Services.IUndoService>();
                    if (undoSvc != null)
                    {
                        undoSvc.Register($"{removed.Count} elemento(s) eliminados", "Deshacer", () =>
                        {
                            try
                            {
                                var colLocal = _selected_collection_safe();
                                foreach (var r in removed.OrderBy(r => r.Index))
                                {
                                    if (r.Item == null) continue;
                                    if (colLocal != null)
                                    {
                                        if (r.Index >= 0 && r.Index <= colLocal.Items.Count)
                                            colLocal.Items.Insert(r.Index, r.Item);
                                        else
                                            colLocal.Items.Add(r.Item);
                                    }
                                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                                    {
                                        if (!CurrentCollectionItems.Contains(r.Item)) CurrentCollectionItems.Add(r.Item);
                                        if (!FilteredItems.Contains(r.Item)) FilteredItems.Add(r.Item);
                                    });
                                }
                                SetCollectionTitle(colLocal != null ? $"{colLocal.Name} ({colLocal.ItemCount} cómics)" : "Selecciona una colección");
                                FavoritesStorage.Save(Collections);
                            }
                            catch (Exception ex)
                            {
                                // ERROR HANDLING MODERNO - Falló el undo bulk (IUndoService)
                                System.Diagnostics.Debug.WriteLine($"Bulk undo (IUndoService) failed: {ex.Message}");
                            }
                        });
                    }
                    else
                    {
                        ComicReader.Services.ToastService.ShowWithUndo($"{removed.Count} elemento(s) eliminados", "Deshacer", () =>
                        {
                            try
                            {
                                var colLocal = _selected_collection_safe();
                                foreach (var r in removed.OrderBy(r => r.Index))
                                {
                                    if (r.Item == null) continue;
                                    if (colLocal != null)
                                    {
                                        if (r.Index >= 0 && r.Index <= colLocal.Items.Count)
                                            colLocal.Items.Insert(r.Index, r.Item);
                                        else
                                            colLocal.Items.Add(r.Item);
                                    }
                                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                                    {
                                        if (!CurrentCollectionItems.Contains(r.Item)) CurrentCollectionItems.Add(r.Item);
                                        if (!FilteredItems.Contains(r.Item)) FilteredItems.Add(r.Item);
                                    });
                                }
                                SetCollectionTitle(colLocal != null ? $"{colLocal.Name} ({colLocal.ItemCount} cómics)" : "Selecciona una colección");
                                FavoritesStorage.Save(Collections);
                            }
                            catch (Exception ex)
                            {
                                // ERROR HANDLING MODERNO - Falló el undo bulk (ToastService)
                                System.Diagnostics.Debug.WriteLine($"Bulk undo (ToastService) failed: {ex.Message}");
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    // ERROR HANDLING MODERNO - Error general en RemoveSelected
                    System.Diagnostics.Debug.WriteLine($"Error in RemoveSelected: {ex.Message}");
                }
            }
        }

        // Punto de extensión futuro: edición de metadatos del cómic

        private void ExportCollections_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Title = "Exportar Colecciones",
                Filter = "Archivo JSON|*.json",
                DefaultExt = "json"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(Collections, new System.Text.Json.JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });
                    File.WriteAllText(saveDialog.FileName, json);
                    FavoritesStorage.Save(Collections);
                    
                    // NOTIFICACIÓN MODERNA premium
                    ComicReader.Services.Notifications.NotificationService.Instance.Success(
                        $"Exportadas {Collections.Count} colecciones a '{Path.GetFileName(saveDialog.FileName)}'", 
                        "Exportación exitosa");
                }
                catch (Exception ex)
                {
                    // ERROR HANDLING MODERNO con ErrorHandler
                    ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(
                        ex, 
                        "Error al exportar colecciones", 
                        ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Notify);
                }
            }
        }

        private void ImportCollections_Click(object sender, RoutedEventArgs e)
        {
            var openDialog = new OpenFileDialog
            {
                Title = "Importar Colecciones",
                Filter = "Archivo JSON|*.json"
            };

            if (openDialog.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(openDialog.FileName);
                    var importedCollections = System.Text.Json.JsonSerializer.Deserialize<ComicCollection[]>(json);
                    
                    if (importedCollections == null || importedCollections.Length == 0)
                    {
                        ComicReader.Services.Notifications.NotificationService.Instance.Warning(
                            "El archivo no contiene colecciones válidas", 
                            "Importación vacía");
                        return;
                    }
                    
                    foreach (var collection in importedCollections)
                    {
                        // Ensure Items is an ObservableCollection after deserialization
                        collection.Items = new ObservableCollection<FavoriteComic>(collection.Items ?? System.Linq.Enumerable.Empty<FavoriteComic>());
                        Collections.Add(collection);
                    }
                    
                    UpdateStatistics();
                    
                    // NOTIFICACIÓN MODERNA premium
                    ComicReader.Services.Notifications.NotificationService.Instance.Success(
                        $"{importedCollections.Length} colecciones importadas exitosamente", 
                        "Importación completa");
                }
                catch (Exception ex)
                {
                    // ERROR HANDLING MODERNO con ErrorHandler
                    ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(
                        ex, 
                        "Error al importar colecciones", 
                        ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Notify);
                }
            }
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void DeleteCollection_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCollection == null)
            {
                ComicReader.Services.Notifications.NotificationService.Instance.Info(
                    "Selecciona una colección primero", 
                    "Colección requerida");
                return;
            }

            // TODO: Reemplazar con diálogo de confirmación moderno (CustomDialog)
            var result = MessageBox.Show($"¿Eliminar la colección '{_selectedCollection.Name}'?\nEsta acción no elimina los archivos físicos.",
                "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                var idx = Collections.IndexOf(_selectedCollection);
                Collections.Remove(_selectedCollection);
                _selectedCollection = null;
                CurrentCollectionItems.Clear();
                FilteredItems.Clear();
                SetCollectionTitle("Selecciona una colección");
                FavoritesStorage.Save(Collections);
                if (Collections.Count > 0)
                {
                    GetCollectionsListBox().SelectedIndex = Math.Min(idx, Collections.Count - 1);
                }
            }
        }

        private void RenameCollection_Click(object sender, RoutedEventArgs e)
        {
            var list = GetCollectionsListBox();
            var col = (ComicCollection) (list.SelectedItem ?? _selectedCollection);
            if (col == null)
            {
                ComicReader.Services.Notifications.NotificationService.Instance.Info(
                    "Selecciona una colección primero", 
                    "Colección requerida");
                return;
            }
            var input = Microsoft.VisualBasic.Interaction.InputBox("Nuevo nombre de la colección:", "Renombrar", col.Name ?? "");
            if (!string.IsNullOrWhiteSpace(input))
            {
                col.Name = input.Trim();
                SetCollectionTitle($"{col.Name} ({col.ItemCount} cómics)");
                FavoritesStorage.Save(Collections);
            }
        }

        private void DuplicateCollection_Click(object sender, RoutedEventArgs e)
        {
            var col = _selectedCollection ?? (GetCollectionsListBox().SelectedItem as ComicCollection);
            if (col == null)
            {
                ComicReader.Services.Notifications.NotificationService.Instance.Info(
                    "Selecciona una colección primero", 
                    "Colección requerida");
                return;
            }
            var copy = new ComicCollection
            {
                Id = Guid.NewGuid(),
                Name = col.Name + " (Copia)",
                Description = col.Description,
                Color = col.Color,
                DateCreated = DateTime.Now,
                    Items = new ObservableCollection<FavoriteComic>(col.Items.Select(i => new FavoriteComic
                    {
                    Id = Guid.NewGuid(),
                    Title = i.Title,
                    Author = i.Author,
                    FilePath = i.FilePath,
                    DateAdded = DateTime.Now,
                    Rating = i.Rating,
                        Tags = new System.Collections.ObjectModel.ObservableCollection<string>(i.Tags ?? System.Linq.Enumerable.Empty<string>()),
                    Notes = i.Notes
                }))
            };
            Collections.Add(copy);
            FavoritesStorage.Save(Collections);
            GetCollectionsListBox().SelectedItem = copy;
        }

        // Invocado desde FavoritesDropHandler cuando el drop es de archivos del
        // Explorador (no un drag interno de reorder). Mantiene la logica original
        // de extension + recursion por carpeta.
        private void AddDroppedFilesToCurrentCollection(IEnumerable<string> paths)
        {
            try
            {
                if (_selectedCollection == null) return;
                if (paths == null) return;
                var exts = new[] { ".cbz", ".cbr", ".cb7", ".cbt", ".zip", ".rar", ".7z", ".tar", ".pdf", ".epub",
                                    ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff", ".avif" };
                foreach (var path in paths.SelectMany(p => Directory.Exists(p)
                                                    ? Directory.GetFiles(p, "*", SearchOption.AllDirectories)
                                                    : new[] { p }))
                {
                    var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                    if (!exts.Contains(ext)) continue;
                    var item = new FavoriteComic
                    {
                        Id = Guid.NewGuid(),
                        Title = System.IO.Path.GetFileNameWithoutExtension(path),
                        FilePath = path,
                        DateAdded = DateTime.Now,
                        Tags = new System.Collections.ObjectModel.ObservableCollection<string>(new[] { "Sin categorizar" })
                    };
                    _selectedCollection.Items.Add(item);
                    CurrentCollectionItems.Add(item);
                }
                ApplyFilter();
                UpdateStatistics();
                FavoritesStorage.Save(Collections);
            }
            catch (Exception ex)
            {
                ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(
                    ex,
                    "Error al arrastrar archivos a la colección",
                    ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Notify);
            }
        }

        // Tras un reorder via GongSolutions DefaultDropHandler, FilteredItems
        // (ItemsSource del ListBox) refleja el nuevo orden, pero el modelo
        // (CurrentCollectionItems y _selectedCollection.Items) sigue con el
        // orden antiguo. Sin sincronizar, ApplyFilter() reconstruye
        // FilteredItems desde el modelo y revierte el reorder. Aqui movemos
        // los items del modelo para que coincidan con el orden visible y
        // persistimos.
        private void SyncModelOrderFromFiltered()
        {
            try
            {
                if (_selectedCollection == null) return;
                if (FilteredItems == null) return;

                // Reordena CurrentCollectionItems segun el orden de FilteredItems,
                // preservando los items que no pasen el filtro al final (no se
                // los puede mover via drag, asi que mantienen su posicion
                // relativa entre si en el orden previo).
                var visibleOrder = FilteredItems.ToList();
                var visibleSet = new HashSet<FavoriteComic>(visibleOrder);
                var hidden = CurrentCollectionItems.Where(x => !visibleSet.Contains(x)).ToList();

                CurrentCollectionItems.Clear();
                foreach (var v in visibleOrder) CurrentCollectionItems.Add(v);
                foreach (var h in hidden) CurrentCollectionItems.Add(h);

                _selectedCollection.Items.Clear();
                foreach (var c in CurrentCollectionItems) _selectedCollection.Items.Add(c);

                FavoritesStorage.Save(Collections);
            }
            catch (Exception ex)
            {
                ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(
                    ex,
                    "Error al guardar el nuevo orden de la colección",
                    ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Notify);
            }
        }

        private void ShowInFolderFromItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el && el.DataContext is FavoriteComic comic && !string.IsNullOrWhiteSpace(comic.FilePath))
            {
                try
                {
                    if (File.Exists(comic.FilePath))
                    {
                        var argument = "/select, \"" + comic.FilePath + "\"";
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = argument,
                            UseShellExecute = true
                        });
                    }
                    else if (Directory.Exists(comic.FilePath))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = comic.FilePath,
                            UseShellExecute = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    // ERROR HANDLING MODERNO con ErrorHandler
                    ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(
                        ex, 
                        "Error al abrir el explorador", 
                        ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Notify);
                }
            }
        }
    }

    // Helpers para resolver los elementos del XAML de forma segura
    public partial class FavoritesWindow
    {
        private ListBox GetCollectionsListBox() => (ListBox)FindName("CollectionsListBox");
        private ListBox GetFavoritesListBox() => (ListBox)FindName("FavoritesListBox");
        private TextBox GetSearchTextBox() => (TextBox)FindName("SearchTextBox");
        private void SetCollectionTitle(string text)
        {
            if (FindName("CollectionTitleText") is TextBlock tb) tb.Text = text;
        }
        private System.Windows.Controls.ComboBox GetTagFilterComboBox() => (System.Windows.Controls.ComboBox)FindName("TagFilterComboBox");
        
        // Safe accessor used by removal/undo handlers to prefer the currently selected listbox item
        private ComicCollection _selected_collection_safe()
        {
            try
            {
                var list = GetCollectionsListBox();
                return (ComicCollection)(list?.SelectedItem ?? _selectedCollection);
            }
            catch
            {
                return _selectedCollection;
            }
        }

        // 🎨 DYNAMIC COVER LOADING (estilo cómic premium)
        /// <summary>
        /// Carga asíncronamente la portada de un cómic desde el archivo
        /// </summary>
        private async System.Threading.Tasks.Task LoadComicCoverAsync(FavoriteComic comic)
        {
            if (comic == null || string.IsNullOrEmpty(comic.FilePath)) return;
            
            try
            {
                await System.Threading.Tasks.Task.Run(async () =>
                {
                    using (var loader = new ComicReader.Services.ComicPageLoader(comic.FilePath))
                    {
                        await loader.LoadComicAsync().ConfigureAwait(false);
                        var cover = await loader.GetCoverThumbnailAsync(300, 450).ConfigureAwait(false);
                        
                        if (cover != null)
                        {
                            // Actualizar en el hilo UI
                            await Dispatcher.InvokeAsync(() =>
                            {
                                comic.ThumbnailImage = cover; // Asignar BitmapImage directamente
                                comic.Thumbnail = cover.UriSource?.ToString() ?? string.Empty; // Mantener string para compatibilidad
                            });
                        }
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error cargando portada para {comic.Title}: {ex.Message}");
                ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(ex, 
                    $"Cargar portada de {comic.Title}", 
                    ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Silent);
            }
        }

        // 📊 ACTUALIZAR ESTADÍSTICAS EN TIEMPO REAL
        private void UpdateStatistics()
        {
            try
            {
                var allComics = Collections.SelectMany(c => c.Items ?? System.Linq.Enumerable.Empty<FavoriteComic>()).ToList();
                
                var totalComics = allComics.Count;
                var completedComics = allComics.Count(c => c.Progress >= 100);
                var readingComics = allComics.Count(c => c.Progress > 0 && c.Progress < 100);
                var ratedComics = allComics.Where(c => c.Rating > 0).ToList();
                var averageRating = ratedComics.Any() ? ratedComics.Average(c => c.Rating) : 0;

                // Actualizar UI
                if (FindName("TotalComicsText") is TextBlock totalText)
                    totalText.Text = totalComics.ToString();

                if (FindName("CompletedComicsText") is TextBlock completedText)
                    completedText.Text = completedComics.ToString();

                if (FindName("ReadingComicsText") is TextBlock readingText)
                    readingText.Text = readingComics.ToString();

                if (FindName("AverageRatingText") is TextBlock ratingText)
                    ratingText.Text = averageRating.ToString("F1");

                if (FindName("StatusText") is TextBlock statusText)
                    statusText.Text = $"Listo · {Collections.Count} colecciones · {totalComics} cómics";
            }
            catch (Exception ex)
            {
                ComicReader.Services.Logger.LogException("Error actualizando estadísticas", ex);
            }
        }
    }

    // Diálogo para crear nueva colección
    public partial class CreateCollectionDialog : Window
    {
        public string CollectionName { get; private set; }
        public string CollectionDescription { get; private set; }
        public string SelectedColor { get; private set; } = "#FF3B82F6";

        public CreateCollectionDialog()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Title = "Nueva Colección";
            Width = 400;
            Height = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var stackPanel = new StackPanel { Margin = new Thickness(20) };
            
            stackPanel.Children.Add(new TextBlock { Text = "Nombre:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) });
            var nameTextBox = new TextBox { Name = "NameTextBox", Padding = new Thickness(8), FontSize = 14 };
            stackPanel.Children.Add(nameTextBox);
            
            stackPanel.Children.Add(new TextBlock { Text = "Descripción:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 15, 0, 5) });
            var descTextBox = new TextBox { Name = "DescriptionTextBox", Padding = new Thickness(8), FontSize = 14, Height = 80, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true };
            stackPanel.Children.Add(descTextBox);

            Grid.SetRow(stackPanel, 0);
            grid.Children.Add(stackPanel);

            var buttonPanel = new StackPanel 
            { 
                Orientation = Orientation.Horizontal, 
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(20)
            };

            var okButton = new Button 
            { 
                Content = "Crear", 
                Width = 80, 
                Height = 35, 
                Margin = new Thickness(5, 0, 5, 0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };
            okButton.Click += (s, e) => 
            {
                CollectionName = nameTextBox.Text?.Trim();
                CollectionDescription = descTextBox.Text?.Trim();
                
                if (string.IsNullOrEmpty(CollectionName))
                {
                    // VALIDACIÓN MODERNA premium
                    ComicReader.Services.Notifications.NotificationService.Instance.Warning(
                        "El nombre de la colección no puede estar vacío", 
                        "Campo requerido");
                    return;
                }
                
                DialogResult = true;
            };

            var cancelButton = new Button 
            { 
                Content = "Cancelar", 
                Width = 80, 
                Height = 35, 
                Margin = new Thickness(5, 0, 5, 0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };
            cancelButton.Click += (s, e) => DialogResult = false;

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, 1);
            grid.Children.Add(buttonPanel);

            Content = grid;
        }
    }
}