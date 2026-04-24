using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ComicReader.Legacy.Models
{
    // Modelo para una colección de cómics
    public class ComicCollection : INotifyPropertyChanged
    {
        private string _name;
        private string _description;
        private string _color;
        private DateTime _dateCreated;
        private ObservableCollection<FavoriteComic> _items;

        public Guid Id { get; set; }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public string Color
        {
            get => _color;
            set { _color = value; OnPropertyChanged(); }
        }

        public DateTime DateCreated
        {
            get => _dateCreated;
            set { _dateCreated = value; OnPropertyChanged(); }
        }

        public ObservableCollection<FavoriteComic> Items
        {
            get => _items ?? (_items = new ObservableCollection<FavoriteComic>());
            set 
            { 
                _items = value; 
                OnPropertyChanged();
                OnPropertyChanged(nameof(ItemCount));
            }
        }

        public int ItemCount => Items?.Count ?? 0;

        public ComicCollection()
        {
            Id = Guid.NewGuid();
            DateCreated = DateTime.Now;
            Items = new ObservableCollection<FavoriteComic>();
            Items.CollectionChanged += (s, e) => OnPropertyChanged(nameof(ItemCount));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Modelo para un cómic favorito
    public class FavoriteComic : INotifyPropertyChanged
    {
        private string _title;
        private string _author;
        private string _filePath;
        private DateTime _dateAdded;
        private int _rating;
        private string[] _tags;
        private string _notes;
        private string _thumbnail;
        private System.Windows.Media.Imaging.BitmapImage _thumbnailImage;
        private int _currentPage;
        private int _totalPages;
        private DateTime? _lastRead;
        private TimeSpan _readingTime;

        public Guid Id { get; set; }

        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        public string Author
        {
            get => _author;
            set { _author = value; OnPropertyChanged(); }
        }

        public string FilePath
        {
            get => _filePath;
            set { _filePath = value; OnPropertyChanged(); }
        }

        public DateTime DateAdded
        {
            get => _dateAdded;
            set { _dateAdded = value; OnPropertyChanged(); }
        }

        public int Rating
        {
            get => _rating;
            set 
            { 
                _rating = Math.Max(0, Math.Min(5, value)); 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(RatingStars));
            }
        }

        public string[] Tags
        {
            get => _tags ?? new string[0];
            set { _tags = value; OnPropertyChanged(); OnPropertyChanged(nameof(TagsText)); }
        }

        public string Notes
        {
            get => _notes;
            set { _notes = value; OnPropertyChanged(); }
        }

        public string Thumbnail
        {
            get => _thumbnail;
            set { _thumbnail = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Imagen de portada del cómic (BitmapImage para binding directo en UI)
        /// </summary>
        public System.Windows.Media.Imaging.BitmapImage ThumbnailImage
        {
            get => _thumbnailImage;
            set { _thumbnailImage = value; OnPropertyChanged(); }
        }

        public int CurrentPage
        {
            get => _currentPage;
            set 
            { 
                var clamped = Math.Max(0, value);
                if (_totalPages > 0) clamped = Math.Min(_totalPages, clamped);
                _currentPage = clamped; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(Progress)); 
            }
        }

        public int TotalPages
        {
            get => _totalPages;
            set 
            { 
                _totalPages = Math.Max(0, value); 
                if (_currentPage > _totalPages) _currentPage = _totalPages;
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(Progress)); 
            }
        }

        public DateTime? LastRead
        {
            get => _lastRead;
            set { _lastRead = value; OnPropertyChanged(); }
        }

        public TimeSpan ReadingTime
        {
            get => _readingTime;
            set { _readingTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReadingTimeText)); }
        }

        // Propiedades calculadas
        public string RatingStars => new string('⭐', Rating) + new string('☆', 5 - Rating);
        public string TagsText => Tags != null ? string.Join(", ", Tags) : "";
        public double Progress
        {
            get
            {
                if (_totalPages <= 0) return 0;
                var pct = (double)_currentPage / _totalPages * 100.0;
                if (double.IsNaN(pct) || double.IsInfinity(pct)) return 0;
                return Math.Max(0, Math.Min(100, pct));
            }
        }
        public string ReadingTimeText => ReadingTime.TotalHours > 1 
            ? $"{ReadingTime.Hours}h {ReadingTime.Minutes}m" 
            : $"{ReadingTime.Minutes}m";

        public FavoriteComic()
        {
            Id = Guid.NewGuid();
            DateAdded = DateTime.Now;
            Tags = new string[0];
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Modelo para estadísticas de colecciones
    public class CollectionStats
    {
        public int TotalCollections { get; set; }
        public int TotalComics { get; set; }
        public int CompletedComics { get; set; }
        public int CurrentlyReading { get; set; }
        public string MostReadAuthor { get; set; }
        public string FavoriteGenre { get; set; }
        public TimeSpan TotalReadingTime { get; set; }
        public double AverageRating { get; set; }

        public static CollectionStats Calculate(ObservableCollection<ComicCollection> collections)
        {
            var allComics = collections.SelectMany(c => c.Items).ToList();
            
            return new CollectionStats
            {
                TotalCollections = collections.Count,
                TotalComics = allComics.Count,
                CompletedComics = allComics.Count(c => c.Progress >= 100),
                CurrentlyReading = allComics.Count(c => c.Progress > 0 && c.Progress < 100),
                MostReadAuthor = allComics.GroupBy(c => c.Author)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key ?? "N/A",
                FavoriteGenre = allComics.SelectMany(c => c.Tags)
                    .GroupBy(t => t)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key ?? "N/A",
                TotalReadingTime = new TimeSpan(allComics.Sum(c => c.ReadingTime.Ticks)),
                AverageRating = allComics.Where(c => c.Rating > 0).DefaultIfEmpty().Average(c => c?.Rating ?? 0)
            };
        }
    }
}