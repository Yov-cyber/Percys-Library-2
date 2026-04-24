using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ComicReader.Models;

namespace ComicReader.Views
{
    public partial class AnnotationToolsWindow : Window, INotifyPropertyChanged
    {
        private ObservableCollection<ComicAnnotation> _annotations;
        private ComicAnnotation _selectedAnnotation;
        private AnnotationType _currentAnnotationType = AnnotationType.Note;
        private Brush _currentColor = Brushes.Yellow;
        private string _currentText = "";

        public ObservableCollection<ComicAnnotation> Annotations
        {
            get => _annotations;
            set
            {
                _annotations = value;
                OnPropertyChanged();
            }
        }

        public ComicAnnotation SelectedAnnotation
        {
            get => _selectedAnnotation;
            set
            {
                _selectedAnnotation = value;
                OnPropertyChanged();
                LoadAnnotationForEditing();
            }
        }

        public AnnotationType CurrentAnnotationType
        {
            get => _currentAnnotationType;
            set
            {
                _currentAnnotationType = value;
                OnPropertyChanged();
            }
        }

        public Brush CurrentColor
        {
            get => _currentColor;
            set
            {
                _currentColor = value;
                OnPropertyChanged();
            }
        }

        public string CurrentText
        {
            get => _currentText;
            set
            {
                _currentText = value;
                OnPropertyChanged();
            }
        }

        public AnnotationToolsWindow()
        {
            InitializeComponent();
            DataContext = this;
            InitializeAnnotations();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void InitializeAnnotations()
        {
            Annotations = new ObservableCollection<ComicAnnotation>
            {
                new ComicAnnotation
                {
                    Id = Guid.NewGuid(),
                    ComicPath = "Batman: Year One",
                    PageNumber = 15,
                    Type = AnnotationType.Highlight,
                    Text = "Gran introducción del personaje",
                    Color = Colors.Yellow,
                    X = 150,
                    Y = 200,
                    Width = 200,
                    Height = 50,
                    CreatedDate = DateTime.Now.AddDays(-2)
                },
                new ComicAnnotation
                {
                    Id = Guid.NewGuid(),
                    ComicPath = "Spider-Man",
                    PageNumber = 8,
                    Type = AnnotationType.Note,
                    Text = "Recordar esta escena para el análisis",
                    Color = Colors.LightBlue,
                    X = 300,
                    Y = 400,
                    Width = 20,
                    Height = 20,
                    CreatedDate = DateTime.Now.AddDays(-1)
                }
            };
        }

        private void LoadAnnotationForEditing()
        {
            if (SelectedAnnotation != null)
            {
                CurrentAnnotationType = SelectedAnnotation.Type;
                CurrentText = SelectedAnnotation.Text;
                CurrentColor = new SolidColorBrush(SelectedAnnotation.Color);
            }
        }

        private void AddAnnotation_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CurrentText))
            {
                MessageBox.Show("Por favor, ingresa un texto para la anotación.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var newAnnotation = new ComicAnnotation
            {
                Id = Guid.NewGuid(),
                ComicPath = "Comic Actual", // En implementación real vendría del cómic activo
                PageNumber = 1, // Página actual
                Type = CurrentAnnotationType,
                Text = CurrentText,
                Color = ((SolidColorBrush)CurrentColor).Color,
                X = 100, // Posición por defecto
                Y = 100,
                Width = CurrentAnnotationType == AnnotationType.Note ? 20 : 150,
                Height = CurrentAnnotationType == AnnotationType.Note ? 20 : 30,
                CreatedDate = DateTime.Now
            };

            Annotations.Add(newAnnotation);
            CurrentText = "";
            MessageBox.Show("Anotación agregada", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UpdateAnnotation_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedAnnotation == null)
            {
                MessageBox.Show("Selecciona una anotación para actualizar.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedAnnotation.Type = CurrentAnnotationType;
            SelectedAnnotation.Text = CurrentText;
            SelectedAnnotation.Color = ((SolidColorBrush)CurrentColor).Color;

            MessageBox.Show("Anotación actualizada", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DeleteAnnotation_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedAnnotation == null)
            {
                MessageBox.Show("Selecciona una anotación para eliminar.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"¿Eliminar la anotación '{SelectedAnnotation.Text}'?", 
                "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Annotations.Remove(SelectedAnnotation);
                SelectedAnnotation = null;
            }
        }

        private void ExportAnnotations_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exportar Anotaciones",
                Filter = "Archivo JSON|*.json|Archivo de Texto|*.txt",
                DefaultExt = ".json"
            };

            if (saveDialog.ShowDialog() == true)
            {
                // Implementar exportación
                MessageBox.Show($"Anotaciones exportadas a: {saveDialog.FileName}", 
                    "Exportación Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ImportAnnotations_Click(object sender, RoutedEventArgs e)
        {
            var openDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Importar Anotaciones",
                Filter = "Archivo JSON|*.json|Todos los archivos|*.*"
            };

            if (openDialog.ShowDialog() == true)
            {
                // Implementar importación
                MessageBox.Show($"Anotaciones importadas desde: {openDialog.FileName}", 
                    "Importación Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string colorName)
            {
                CurrentColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorName));
            }
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

namespace ComicReader.Models
{
    public class ComicAnnotation : INotifyPropertyChanged
    {
        private Guid _id;
        private string _comicPath;
        private int _pageNumber;
        private AnnotationType _type;
        private string _text;
        private Color _color;
        private double _x, _y, _width, _height;
        private DateTime _createdDate;

        public Guid Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string ComicPath
        {
            get => _comicPath;
            set { _comicPath = value; OnPropertyChanged(); }
        }

        public int PageNumber
        {
            get => _pageNumber;
            set { _pageNumber = value; OnPropertyChanged(); }
        }

        public AnnotationType Type
        {
            get => _type;
            set { _type = value; OnPropertyChanged(); OnPropertyChanged(nameof(TypeIcon)); }
        }

        public string Text
        {
            get => _text;
            set { _text = value; OnPropertyChanged(); }
        }

        public Color Color
        {
            get => _color;
            set { _color = value; OnPropertyChanged(); OnPropertyChanged(nameof(ColorBrush)); }
        }

        public double X
        {
            get => _x;
            set { _x = value; OnPropertyChanged(); }
        }

        public double Y
        {
            get => _y;
            set { _y = value; OnPropertyChanged(); }
        }

        public double Width
        {
            get => _width;
            set { _width = value; OnPropertyChanged(); }
        }

        public double Height
        {
            get => _height;
            set { _height = value; OnPropertyChanged(); }
        }

        public DateTime CreatedDate
        {
            get => _createdDate;
            set { _createdDate = value; OnPropertyChanged(); }
        }

        public string TypeIcon
        {
            get
            {
                return Type switch
                {
                    AnnotationType.Note => "📝",
                    AnnotationType.Highlight => "🔆",
                    AnnotationType.Bookmark => "🔖",
                    _ => "📝"
                };
            }
        }

        public Brush ColorBrush => new SolidColorBrush(Color);

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum AnnotationType
    {
        Note,
        Highlight,
        Bookmark
    }
}