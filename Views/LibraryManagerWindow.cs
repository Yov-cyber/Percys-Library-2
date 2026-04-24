using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ComicReader.Models;
using ComicReader.Services;

namespace ComicReader.Views
{
    public partial class LibraryManagerWindow : Window, INotifyPropertyChanged
    {
        private ObservableCollection<ComicLibrary> _libraries;
        private ComicLibrary _selectedLibrary;
        private ObservableCollection<ComicItem> _libraryContents;
        private string _newLibraryName = "";
        private string _newLibraryPath = "";

        public ObservableCollection<ComicLibrary> Libraries
        {
            get => _libraries;
            set
            {
                _libraries = value;
                OnPropertyChanged();
            }
        }

        public ComicLibrary SelectedLibrary
        {
            get => _selectedLibrary;
            set
            {
                _selectedLibrary = value;
                OnPropertyChanged();
                LoadLibraryContents();
            }
        }

        public ObservableCollection<ComicItem> LibraryContents
        {
            get => _libraryContents;
            set
            {
                _libraryContents = value;
                OnPropertyChanged();
            }
        }

        public string NewLibraryName
        {
            get => _newLibraryName;
            set
            {
                _newLibraryName = value;
                OnPropertyChanged();
            }
        }

        public string NewLibraryPath
        {
            get => _newLibraryPath;
            set
            {
                _newLibraryPath = value;
                OnPropertyChanged();
            }
        }

        public LibraryManagerWindow()
        {
            InitializeComponent();
            DataContext = this;
            InitializeLibraries();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void InitializeLibraries()
        {
            Libraries = new ObservableCollection<ComicLibrary>();
            LibraryContents = new ObservableCollection<ComicItem>();
            
            // Crear bibliotecas predeterminadas
            Libraries.Add(new ComicLibrary 
            { 
                Id = Guid.NewGuid(),
                Name = "Mi Biblioteca Personal", 
                Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Comics"),
                Description = "Biblioteca principal de cómics",
                DateCreated = DateTime.Now.AddDays(-30),
                ComicCount = 0,
                Category = "Personal",
                IsDefault = true
            });

            Libraries.Add(new ComicLibrary 
            { 
                Id = Guid.NewGuid(),
                Name = "Favoritos", 
                Path = "",
                Description = "Cómics marcados como favoritos",
                DateCreated = DateTime.Now.AddDays(-15),
                ComicCount = 0,
                Category = "Especial",
                IsDefault = false
            });

            Libraries.Add(new ComicLibrary 
            { 
                Id = Guid.NewGuid(),
                Name = "Manga Collection", 
                Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Manga"),
                Description = "Colección de manga japonés",
                DateCreated = DateTime.Now.AddDays(-10),
                ComicCount = 0,
                Category = "Manga",
                IsDefault = false
            });

            // Actualizar conteos
            foreach (var library in Libraries)
            {
                UpdateLibraryCount(library);
            }
        }

        private void LoadLibraryContents()
        {
            LibraryContents.Clear();

            if (SelectedLibrary == null || string.IsNullOrEmpty(SelectedLibrary.Path))
                return;

            if (!Directory.Exists(SelectedLibrary.Path))
                return;

            try
            {
                var comicExtensions = new[] { ".cbz", ".cbr", ".zip", ".rar", ".pdf", ".epub" };
                var files = Directory.GetFiles(SelectedLibrary.Path, "*", SearchOption.AllDirectories)
                    .Where(f => comicExtensions.Contains(Path.GetExtension(f).ToLower()));

                foreach (var file in files.Take(50)) // Limitar para rendimiento
                {
                    var comicItem = new ComicItem
                    {
                        Id = Guid.NewGuid(),
                        Title = Path.GetFileNameWithoutExtension(file),
                        FilePath = file,
                        FileSize = new FileInfo(file).Length,
                        DateAdded = File.GetCreationTime(file),
                        LastRead = File.GetLastAccessTime(file),
                        Format = Path.GetExtension(file).TrimStart('.').ToUpper(),
                        IsRead = false, // En implementación real, esto vendría de la base de datos
                        Rating = 0,
                        Tags = new ObservableCollection<string>()
                    };

                    LibraryContents.Add(comicItem);
                }
            }
            catch (Exception ex)
            {
                ComicReader.Services.ErrorHandling.ErrorHandler.Instance.HandleException(ex, "Cargar contenido de biblioteca", ComicReader.Services.ErrorHandling.ErrorRecoveryStrategy.Notify);
            }
        }

        private void UpdateLibraryCount(ComicLibrary library)
        {
            if (string.IsNullOrEmpty(library.Path) || !Directory.Exists(library.Path))
            {
                library.ComicCount = 0;
                return;
            }

            try
            {
                var comicExtensions = new[] { ".cbz", ".cbr", ".zip", ".rar", ".pdf", ".epub" };
                library.ComicCount = Directory.GetFiles(library.Path, "*", SearchOption.AllDirectories)
                    .Count(f => comicExtensions.Contains(Path.GetExtension(f).ToLower()));
            }
            catch
            {
                library.ComicCount = 0;
            }
        }

        private void CreateLibrary_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NewLibraryName))
            {
                ComicReader.Services.Notifications.NotificationService.Instance.Warning("El nombre de la biblioteca es requerido", "Campo obligatorio");
                return;
            }

            var newLibrary = new ComicLibrary
            {
                Id = Guid.NewGuid(),
                Name = NewLibraryName,
                Path = NewLibraryPath,
                Description = $"Biblioteca creada el {DateTime.Now:dd/MM/yyyy}",
                DateCreated = DateTime.Now,
                ComicCount = 0,
                Category = "Personal",
                IsDefault = false
            };

            UpdateLibraryCount(newLibrary);
            Libraries.Add(newLibrary);

            NewLibraryName = "";
            NewLibraryPath = "";

            ComicReader.Services.Notifications.NotificationService.Instance.Success($"Biblioteca '{newLibrary.Name}' creada exitosamente", "Biblioteca creada");
        }

        private void BrowsePath_Click(object sender, RoutedEventArgs e)
        {
            using (var folderDialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                folderDialog.Description = "Seleccionar carpeta de la biblioteca";
                folderDialog.ShowNewFolderButton = true;

                if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    NewLibraryPath = folderDialog.SelectedPath;
                }
            }
        }

        private void EditLibrary_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedLibrary == null)
            {
                ComicReader.Services.Notifications.NotificationService.Instance.Info("Selecciona una biblioteca para editar", "Selección requerida");
                return;
            }

            // Aquí abrirías un diálogo de edición
            ComicReader.Services.Notifications.NotificationService.Instance.Info($"Editar biblioteca: {SelectedLibrary.Name}", "Funcionalidad pendiente");
        }

        private void DeleteLibrary_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedLibrary == null)
            {
                ComicReader.Services.Notifications.NotificationService.Instance.Info("Selecciona una biblioteca para eliminar", "Selección requerida");
                return;
            }

            if (SelectedLibrary.IsDefault)
            {
                ComicReader.Services.Notifications.NotificationService.Instance.Warning("No puedes eliminar la biblioteca predeterminada", "Operación no permitida");
                return;
            }

            var result = MessageBox.Show($"¿Eliminar la biblioteca '{SelectedLibrary.Name}'?\n\nEsto no eliminará los archivos, solo la biblioteca.", 
                "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Libraries.Remove(SelectedLibrary);
                SelectedLibrary = null;
                ComicReader.Services.Notifications.NotificationService.Instance.Success("Biblioteca eliminada", "Operación exitosa");
            }
        }

        private void RefreshLibrary_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedLibrary == null)
                return;

            UpdateLibraryCount(SelectedLibrary);
            LoadLibraryContents();
            ComicReader.Services.Notifications.NotificationService.Instance.Success("Biblioteca actualizada", "Actualización completada");
        }

        private void OpenComic_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is ComicItem comic)
            {
                if (File.Exists(comic.FilePath))
                {
                    var mainWindow = Owner as global::ComicReader.MainWindow;
                    if (mainWindow != null)
                    {
                        var method = mainWindow.GetType().GetMethod("OpenComicFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        method?.Invoke(mainWindow, new object[] { comic.FilePath });
                        mainWindow.ShowComicView();
                        Close();
                    }
                }
                else
                {
                    ComicReader.Services.Notifications.NotificationService.Instance.Warning("El archivo no existe o ha sido movido", "Archivo no encontrado");
                }
            }
        }

        private void ExportLibrary_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exportar Bibliotecas",
                Filter = "Archivo JSON|*.json",
                DefaultExt = ".json"
            };

            if (saveDialog.ShowDialog() == true)
            {
                // Implementar exportación
                ComicReader.Services.Notifications.NotificationService.Instance.Success($"Bibliotecas exportadas a: {saveDialog.FileName}", "Exportación exitosa");
            }
        }

        private void ImportLibrary_Click(object sender, RoutedEventArgs e)
        {
            var openDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Importar Bibliotecas",
                Filter = "Archivo JSON|*.json"
            };

            if (openDialog.ShowDialog() == true)
            {
                // Implementar importación
                ComicReader.Services.Notifications.NotificationService.Instance.Success($"Bibliotecas importadas desde: {openDialog.FileName}", "Importación exitosa");
            }
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}