using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ComicReader.Views
{
    public partial class FileExplorerView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public event Action<string> FileSelected;
        public event Action<string> FolderChanged;

        private ObservableCollection<FileItem> _currentItems = new ObservableCollection<FileItem>();
    private List<FileItem> _allItems = new List<FileItem>();
        private ObservableCollection<string> _recentFolders = new ObservableCollection<string>();
        private string _currentPath = "";
        private bool _showHiddenFiles = false;
        private string _searchFilter = "";

        // Filtros de archivos de cómics
        private readonly string[] _comicExtensions = { ".cbz", ".cbr", ".pdf", ".epub", ".cb7", ".cbt", ".zip", ".rar", ".7z" };
        private readonly string[] _imageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff" };

        public ObservableCollection<FileItem> CurrentItems
        {
            get => _currentItems;
            set { _currentItems = value; OnPropertyChanged(nameof(CurrentItems)); }
        }

        public ObservableCollection<string> RecentFolders
        {
            get => _recentFolders;
            set { _recentFolders = value; OnPropertyChanged(nameof(RecentFolders)); }
        }

        public string CurrentPath
        {
            get => _currentPath;
            set 
            { 
                _currentPath = value; 
                OnPropertyChanged(nameof(CurrentPath));
                OnPropertyChanged(nameof(CanGoUp));
                // Forzar reevaluación de CanExecute en comandos como GoUpCommand
                CommandManager.InvalidateRequerySuggested();
                LoadCurrentDirectory();
            }
        }

        public bool ShowHiddenFiles
        {
            get => _showHiddenFiles;
            set 
            { 
                _showHiddenFiles = value; 
                OnPropertyChanged(nameof(ShowHiddenFiles));
                LoadCurrentDirectory();
            }
        }

        public string SearchFilter
        {
            get => _searchFilter;
            set 
            { 
                _searchFilter = value; 
                OnPropertyChanged(nameof(SearchFilter));
                ApplyFilter();
            }
        }

        public bool CanGoUp => !string.IsNullOrEmpty(CurrentPath) && Directory.GetParent(CurrentPath) != null;

    public ICommand GoUpCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand GoToFolderCommand { get; }
    public ICommand AddToFavoritesCommand { get; }

        public FileExplorerView()
        {
            #pragma warning disable
#if !DESIGN_TIME
            InitializeComponent();
#endif
            #pragma warning restore
            DataContext = this;

            GoUpCommand = new LocalRelayCommand(GoUp, () => CanGoUp);
            RefreshCommand = new LocalRelayCommand(Refresh);
            GoToFolderCommand = new LocalRelayCommand<string>(GoToFolder);
            AddToFavoritesCommand = new LocalRelayCommand(() => AddCurrentToFavorites());
            ItemDoubleClickCommand = new LocalRelayCommand<FileItem>(ItemDoubleClick);

            // Inicializar con el directorio actual
            CurrentPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            LoadRecentFolders();
        }

        private void LoadCurrentDirectory()
        {
            try
            {
                if (!Directory.Exists(CurrentPath))
                {
                    CurrentPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    return;
                }

                var items = new List<FileItem>();

                // Agregar carpetas
                var directories = Directory.GetDirectories(CurrentPath);
                foreach (var dir in directories.OrderBy(d => Path.GetFileName(d)))
                {
                    var dirInfo = new DirectoryInfo(dir);
                    
                    // Filtrar archivos ocultos si está desactivado
                    if (!ShowHiddenFiles && dirInfo.Attributes.HasFlag(FileAttributes.Hidden))
                        continue;

                    items.Add(new FileItem
                    {
                        Name = Path.GetFileName(dir),
                        FullPath = dir,
                        IsDirectory = true,
                        Size = GetDirectorySize(dir),
                        DateModified = dirInfo.LastWriteTime,
                        Icon = GetFolderIcon()
                    });
                }

                // Agregar archivos
                var files = Directory.GetFiles(CurrentPath);
                foreach (var file in files.OrderBy(f => Path.GetFileName(f)))
                {
                    var fileInfo = new FileInfo(file);
                    var extension = Path.GetExtension(file).ToLowerInvariant();

                    // Filtrar archivos ocultos si está desactivado
                    if (!ShowHiddenFiles && fileInfo.Attributes.HasFlag(FileAttributes.Hidden))
                        continue;

                    // Solo mostrar archivos de cómics e imágenes
                    if (!_comicExtensions.Contains(extension) && !_imageExtensions.Contains(extension))
                        continue;

                    items.Add(new FileItem
                    {
                        Name = Path.GetFileName(file),
                        FullPath = file,
                        IsDirectory = false,
                        Size = fileInfo.Length,
                        DateModified = fileInfo.LastWriteTime,
                        Icon = GetFileIcon(extension),
                        IsComicFile = _comicExtensions.Contains(extension)
                    });
                }

                // Actualizar la fuente completa y aplicar filtro si corresponde
                _allItems = items;
                if (string.IsNullOrWhiteSpace(SearchFilter))
                {
                    CurrentItems = new ObservableCollection<FileItem>(_allItems);
                }
                else
                {
                    ApplyFilter();
                }
                AddToRecentFolders(CurrentPath);
                FolderChanged?.Invoke(CurrentPath);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show($"No tienes permisos para acceder a la carpeta: {CurrentPath}", 
                              "Acceso Denegado", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar la carpeta: {ex.Message}", 
                              "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Refresh()
        {
            // LoadCurrentDirectory ya re-aplica el filtro si SearchFilter no está vacío
            LoadCurrentDirectory();
        }

        private void ApplyFilter()
        {
            if (string.IsNullOrWhiteSpace(SearchFilter))
            {
                // Si no hay filtro, mostrar todos los elementos cargados
                CurrentItems = new ObservableCollection<FileItem>(_allItems);
                return;
            }

            // Filtrar SIEMPRE desde la fuente completa para evitar filtrado incremental
            var term = SearchFilter.Trim().ToLowerInvariant();
            var filtered = _allItems.Where(item =>
                (item.Name ?? string.Empty).ToLowerInvariant().Contains(term))
                .ToList();

            CurrentItems = new ObservableCollection<FileItem>(filtered);
        }

        private void GoUp()
        {
            var parent = Directory.GetParent(CurrentPath);
            if (parent != null)
            {
                CurrentPath = parent.FullName;
            }
        }

        private void GoToFolder(string path)
        {
            if (Directory.Exists(path))
            {
                CurrentPath = path;
            }
        }

        private void AddCurrentToFavorites()
        {
            if (!RecentFolders.Contains(CurrentPath))
            {
                RecentFolders.Insert(0, CurrentPath);
                if (RecentFolders.Count > 10)
                {
                    RecentFolders.RemoveAt(RecentFolders.Count - 1);
                }
                SaveRecentFolders();
            }
        }

        private void AddToRecentFolders(string path)
        {
            if (RecentFolders.Contains(path))
            {
                RecentFolders.Remove(path);
            }
            
            RecentFolders.Insert(0, path);
            
            if (RecentFolders.Count > 10)
            {
                RecentFolders.RemoveAt(RecentFolders.Count - 1);
            }
            
            SaveRecentFolders();
        }

        private void LoadRecentFolders()
        {
            try
            {
                var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                                               "PercysLibrary", "RecentFolders.txt");
                
                if (File.Exists(settingsPath))
                {
                    var folders = File.ReadAllLines(settingsPath);
                    foreach (var folder in folders.Where(Directory.Exists))
                    {
                        RecentFolders.Add(folder);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading recent folders: {ex.Message}");
            }
        }

        private void SaveRecentFolders()
        {
            try
            {
                var settingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PercysLibrary");
                Directory.CreateDirectory(settingsDir);
                
                var settingsPath = Path.Combine(settingsDir, "RecentFolders.txt");
                File.WriteAllLines(settingsPath, RecentFolders);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving recent folders: {ex.Message}");
            }
        }

        private long GetDirectorySize(string path)
        {
            try
            {
                return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                               .Where(f => _comicExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                               .Sum(f => new FileInfo(f).Length);
            }
            catch
            {
                return 0;
            }
        }

        private ImageSource GetFolderIcon()
        {
            return LoadAppIcon();
        }

        private ImageSource GetFileIcon(string extension)
        {
            // Podríamos mapear por extensión a distintos iconos.
            // Por ahora, reutilizamos el icono de la app para todos los archivos compatibles.
            return LoadAppIcon();
        }

        private static ImageSource _appIconCache;

        private static ImageSource LoadAppIcon()
        {
            try
            {
                if (_appIconCache != null)
                    return _appIconCache;

                var uri = new Uri("pack://application:,,,/icono.ico", UriKind.Absolute);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = uri;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                _appIconCache = bmp;
                return _appIconCache;
            }
            catch
            {
                return null;
            }
        }

        public ICommand ItemDoubleClickCommand { get; }

        public void ItemDoubleClick(FileItem item)
        {
            if (item.IsDirectory)
            {
                CurrentPath = item.FullPath;
            }
            else if (item.IsComicFile)
            {
                FileSelected?.Invoke(item.FullPath);
            }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class FileItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private string _name;
        private string _fullPath;
        private bool _isDirectory;
        private long _size;
        private DateTime _dateModified;
        private ImageSource _icon;
        private bool _isComicFile;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        public string FullPath
        {
            get => _fullPath;
            set { _fullPath = value; OnPropertyChanged(nameof(FullPath)); }
        }

        public bool IsDirectory
        {
            get => _isDirectory;
            set { _isDirectory = value; OnPropertyChanged(nameof(IsDirectory)); }
        }

        public long Size
        {
            get => _size;
            set { _size = value; OnPropertyChanged(nameof(Size)); OnPropertyChanged(nameof(SizeText)); }
        }

        public string SizeText => IsDirectory ? $"({GetItemCount()} elementos)" : FormatFileSize(Size);

        public DateTime DateModified
        {
            get => _dateModified;
            set { _dateModified = value; OnPropertyChanged(nameof(DateModified)); }
        }

        public ImageSource Icon
        {
            get => _icon;
            set { _icon = value; OnPropertyChanged(nameof(Icon)); }
        }

        public bool IsComicFile
        {
            get => _isComicFile;
            set { _isComicFile = value; OnPropertyChanged(nameof(IsComicFile)); }
        }

        private int GetItemCount()
        {
            try
            {
                if (IsDirectory && Directory.Exists(FullPath))
                {
                    return Directory.GetFileSystemEntries(FullPath).Length;
                }
            }
            catch { }
            return 0;
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024 * 1024):F1} MB";
            return $"{bytes / (1024 * 1024 * 1024):F1} GB";
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Clase RelayCommand local para evitar conflictos con ComicReader.Commands.RelayCommand
    public class LocalRelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public LocalRelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object parameter) => _execute?.Invoke();
    }

    public class LocalRelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        public LocalRelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke((T)parameter) ?? true;
        public void Execute(object parameter) => _execute?.Invoke((T)parameter);
    }
}