using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using ComicReader.Core.Abstractions;
using ComicReader.Services;
using ComicReader.Commands;
using System.Linq;

namespace ComicReader.ViewModels
{
    public class CollectionsViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private readonly ICollectionService _service;
    private readonly Action<string, string, Action> _toastInvoker;
    private readonly Services.IUndoService _undoService;
        private const string ThumbCacheVersion = "v2";
        private readonly System.Threading.SemaphoreSlim _thumbSemaphore = new System.Threading.SemaphoreSlim(2);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.Tasks.Task> _thumbTasks = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.Tasks.Task>(StringComparer.OrdinalIgnoreCase);
    private readonly Services.ThumbnailManager _thumbnailManager;

    public ObservableCollection<CollectionDto> Collections { get; } = new ObservableCollection<CollectionDto>();
    private CollectionDto _selectedCollection;
    // Expose a VM collection for the selected collection items so the UI can bind to INotifyPropertyChanged items
    public System.Collections.ObjectModel.ObservableCollection<ComicItemViewModel> SelectedItems { get; } = new System.Collections.ObjectModel.ObservableCollection<ComicItemViewModel>();
        public CollectionDto SelectedCollection
        {
            get => _selectedCollection;
            set
            {
                if (_selectedCollection == value) return;
                _selectedCollection = value;
                OnPropertyChanged(nameof(SelectedCollection));
                // populate SelectedItems viewmodel collection from DTOs
                SelectedItems.Clear();
                if (_selectedCollection?.Items != null)
                {
                    foreach (var it in _selectedCollection.Items)
                    {
                        SelectedItems.Add(new ComicItemViewModel(it));
                    }
                }
                // Notify command system that CanExecute may have changed
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                // kick off thumbnail population for selected collection
                if (_selectedCollection != null)
                {
                    _ = EnsureThumbnailsForSelectedAsync(_selectedCollection);
                }
            }
        }

        public ICommand NewCommand { get; }
        public ICommand RenameCommand { get; }
        public ICommand DuplicateCommand { get; }
        public ICommand DeleteCommand { get; }
    public System.Windows.Input.ICommand OpenFavoritesCommand { get; }

    // Aggregated favorites across all collections (DTOs implement INotifyPropertyChanged)
    public System.Collections.ObjectModel.ObservableCollection<Core.Abstractions.ComicItemDto> FavoriteItems { get; } = new System.Collections.ObjectModel.ObservableCollection<Core.Abstractions.ComicItemDto>();

        // Default constructor uses JSON service for app runtime
    public CollectionsViewModel() : this(new CollectionServiceJson(), ToastService.Show) { }

        // Back-compat constructor (service only)
        public CollectionsViewModel(ICollectionService service) : this(service, ToastService.Show) { }

        // Testable constructor allowing DI of a mock service and toast invoker
        public CollectionsViewModel(ICollectionService service, Action<string, string, Action> toastInvoker)
            : this(service, new Services.UndoService(toastInvoker))
        {
        }

        // New constructor accepting an IUndoService for better testability and centralization
        public CollectionsViewModel(ICollectionService service, Services.IUndoService undoService)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _undoService = undoService ?? throw new ArgumentNullException(nameof(undoService));
            _toastInvoker = ToastService.Show;
            // Prefer a global ThumbnailManager registered in ServiceLocator; fall back to a local instance.
            try
            {
                var global = ComicReader.Core.Services.ServiceLocator.TryGet<Services.ThumbnailManager>();
                if (global != null)
                {
                    _thumbnailManager = global;
                }
                else
                {
                    var concurrency = 2;
                    try { concurrency = SettingsManager.Settings?.ConcurrencyCap ?? 2; } catch { }
                    var maxFiles = 500;
                    try { maxFiles = SettingsManager.Settings?.ThumbCacheMaxFiles ?? 500; } catch { }
                    var maxBytes = 200 * 1024 * 1024L;
                    try { maxBytes = SettingsManager.Settings?.ThumbCacheMaxBytes ?? maxBytes; } catch { }
                    _thumbnailManager = new Services.ThumbnailManager(Math.Max(1, concurrency), Math.Max(1, maxFiles), Math.Max(1024, maxBytes));
                }
            }
            catch
            {
                _thumbnailManager = new Services.ThumbnailManager(2);
            }
            NewCommand = new RelayCommand(_ => NewCollection());
            RenameCommand = new RelayCommand(p => Rename(p as CollectionDto), p => p is CollectionDto);
            DuplicateCommand = new RelayCommand(p => Duplicate(p as CollectionDto), p => p is CollectionDto);
            DeleteCommand = new RelayCommand(p => Delete(p as CollectionDto), p => p is CollectionDto);
            OpenFavoritesCommand = new RelayCommand(_ => { /* view will open window; command placeholder */ });
            Load();
            RefreshFavorites();
        }

        private void Load()
        {
            Collections.Clear();
            foreach (var c in _service.GetAll()) Collections.Add(c);
            RefreshFavorites();
        }

        private void NewCollection()
        {
            var req = new CollectionCreateRequest { Name = "Nueva Colección", Description = string.Empty };
            var created = _service.Create(req);
            if (created != null) Collections.Add(created);
        }

        // Public helper used by the view code-behind when creating via dialog
        public void CreateFromRequest(CollectionCreateRequest req)
        {
            if (req == null) return;
            var created = _service.Create(req);
            if (created != null) Collections.Add(created);
            RefreshFavorites();
        }

        public void ImportFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                _service.ImportCollections(path);
                Load();
                RefreshFavorites();
            }
            catch { }
        }

        public void ExportToFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                _service.ExportCollections(path);
            }
            catch { }
        }

        public void UpdateFromRequest(Guid id, CollectionCreateRequest req)
        {
            if (req == null) return;
            try
            {
                var updated = _service.Update(id, req);
                if (updated != null)
                {
                    var idx = Collections.IndexOf(Collections.First(x => x.Id == id));
                    Collections[idx] = updated;
                    OnPropertyChanged(nameof(Collections));
                    // if the updated collection is currently selected, refresh SelectedCollection so UI VMs update
                    if (_selectedCollection != null && _selectedCollection.Id == id)
                    {
                        SelectedCollection = updated;
                    }
                    RefreshFavorites();
                }
            }
            catch { }
        }

        private void Rename(CollectionDto c)
        {
            if (c == null) return;
            var newName = c.Name + " (renombrada)";
            var updated = _service.Rename(c.Id, newName);
            if (updated != null)
            {
                var idx = Collections.IndexOf(Collections.First(x => x.Id == c.Id));
                Collections[idx] = updated;
                OnPropertyChanged(nameof(Collections));
            }
        }

        private void Duplicate(CollectionDto c)
        {
            if (c == null) return;
            var copy = _service.Duplicate(c.Id);
            if (copy != null) Collections.Add(copy);
        }

        private void Delete(CollectionDto c)
        {
            if (c == null) return;

            // capture original index so undo can restore position
            var idx = Collections.IndexOf(c);

            // take a snapshot of the DTO so we can restore it if the user undoes
            var dto = new CollectionDto
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                CoverPath = c.CoverPath,
                Count = c.Count,
                Items = c.Items?.Select(i => new ComicItemDto { Title = i.Title, Path = i.Path, ThumbPath = i.ThumbPath, IsFavorite = i.IsFavorite }).ToList() ?? new System.Collections.Generic.List<ComicItemDto>()
            };

            _service.Delete(c.Id);
            Collections.Remove(c);

            // Register undo via the central undo service so UI and tests can trigger it consistently
            try
            {
                _undoService?.Register($"Colección \"{dto.Name}\" eliminada", "Deshacer", () =>
                {
                    try
                    {
                        _service.AddRaw(dto, idx);
                        Load();
                    }
                    catch (Exception ex)
                    {
                        try { Console.WriteLine($"[CollectionsViewModel] Undo restore failed: {ex}"); } catch { }
                    }
                });
            }
            catch { }
        }

        // Public wrappers for view code-behind (distinct names to avoid conflict)
        public void DuplicateCollection(CollectionDto c)
        {
            Duplicate(c);
        }

        public void DeleteCollection(CollectionDto c)
        {
            Delete(c);
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        // Thumbnail helpers (mirror logic used elsewhere in the app)
        private string TryGetCachedThumbPath(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return null;
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var dir = System.IO.Path.Combine(appData, "PercysLibrary", "Thumbs");
                System.IO.Directory.CreateDirectory(dir);
                using (var sha1 = System.Security.Cryptography.SHA1.Create())
                {
                    var key = ThumbCacheVersion + "|" + filePath;
                    var hash = BitConverter.ToString(sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key))).Replace("-", string.Empty);
                    var p = System.IO.Path.Combine(dir, hash + ".png");
                    return System.IO.File.Exists(p) ? p : null;
                }
            }
            catch { return null; }
        }

        private string ComputeThumbCachePath(string filePath)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = System.IO.Path.Combine(appData, "PercysLibrary", "Thumbs");
            System.IO.Directory.CreateDirectory(dir);
            using (var sha1 = System.Security.Cryptography.SHA1.Create())
            {
                var key = ThumbCacheVersion + "|" + filePath;
                var hash = BitConverter.ToString(sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key))).Replace("-", string.Empty);
                return System.IO.Path.Combine(dir, hash + ".png");
            }
        }

        private System.Threading.Tasks.Task EnsureThumbnailsForSelectedAsync(CollectionDto col)
        {
            if (col?.Items == null || col.Items.Count == 0) return System.Threading.Tasks.Task.CompletedTask;
            foreach (var item in col.Items)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(item.ThumbPath) && System.IO.File.Exists(item.ThumbPath)) continue;
                    var cached = TryGetCachedThumbPath(item.Path);
                    if (!string.IsNullOrWhiteSpace(cached))
                    {
                        item.ThumbPath = cached;
                        // update corresponding VM if present
                        var vm = SelectedItems.FirstOrDefault(v => string.Equals(v.Path, item.Path, StringComparison.OrdinalIgnoreCase));
                        if (vm != null) System.Windows.Application.Current?.Dispatcher?.Invoke(() => vm.ThumbPath = cached);
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(item.Path)) continue;
                    if (!System.IO.File.Exists(item.Path) && !System.IO.Directory.Exists(item.Path)) continue;

                    // schedule generation (avoid duplicate tasks)
                    if (!_thumbTasks.ContainsKey(item.Path))
                    {
                        // Ask the thumbnail manager to generate and call back when ready so VM and DTO can update
                        try
                        {
                            _thumbnailManager.EnqueueGenerate(item.Path, (resultPath) =>
                            {
                                if (!string.IsNullOrWhiteSpace(resultPath))
                                {
                                    // update model and VM on UI thread
                                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                                    {
                                        try
                                        {
                                            item.ThumbPath = resultPath;
                                            var vm = SelectedItems.FirstOrDefault(v => string.Equals(v.Path, item.Path, StringComparison.OrdinalIgnoreCase));
                                            if (vm != null) vm.ThumbPath = resultPath;
                                            try
                                            {
                                                var req = new CollectionCreateRequest { Name = col.Name, Description = col.Description, CoverPath = col.CoverPath, Items = col.Items };
                                                _service.Update(col.Id, req);
                                            }
                                            catch { }
                                        }
                                        catch { }
                                    });
                                }

                                // EnqueueGenerate espera un Task devolviendo, así que devolvemos un Task completado
                                return System.Threading.Tasks.Task.CompletedTask;
                            });
                        }
                        catch { }
                        // track a dummy task so we don't schedule twice
                        _thumbTasks.TryAdd(item.Path, System.Threading.Tasks.Task.CompletedTask);
                    }
                }
                catch { }
            }

            return System.Threading.Tasks.Task.CompletedTask;
        }

        // Rebuild the FavoriteItems collection from current Collections
        public void RefreshFavorites()
        {
            try
            {
                FavoriteItems.Clear();
                foreach (var c in Collections)
                {
                    if (c?.Items == null) continue;
                    foreach (var it in c.Items)
                    {
                        try
                        {
                            if (it != null && it.IsFavorite)
                            {
                                FavoriteItems.Add(it);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private async System.Threading.Tasks.Task GenerateAndSaveThumbAsync(string filePath, ComicItemDto item, CollectionDto col)
        {
            await _thumbSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                System.Windows.Media.Imaging.BitmapSource cover = null;
                try
                {
                    using (var loader = new ComicReader.Services.ComicPageLoader(filePath))
                    {
                        await loader.LoadComicAsync().ConfigureAwait(false);
                        cover = await loader.GetCoverThumbnailAsync(300, 400).ConfigureAwait(false);
                    }
                }
                catch { }

                if (cover != null)
                {
                    try
                    {
                        var path = ComputeThumbCachePath(filePath);
                        using (var fs = System.IO.File.Open(path, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.Read))
                        {
                            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(cover));
                            enc.Save(fs);
                        }

                        // update model and persist collection
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                item.ThumbPath = path;
                                // update VM if present
                                var vm = SelectedItems.FirstOrDefault(v => string.Equals(v.Path, item.Path, StringComparison.OrdinalIgnoreCase));
                                if (vm != null) vm.ThumbPath = path;
                                // persist collection change
                                try
                                {
                                    var req = new CollectionCreateRequest { Name = col.Name, Description = col.Description, CoverPath = col.CoverPath, Items = col.Items };
                                    _service.Update(col.Id, req);
                                }
                                catch { }
                            }
                            catch { }
                        });
                    }
                    catch { }
                }
            }
            finally
            {
                _thumbSemaphore.Release();
            }
        }
    }
}
