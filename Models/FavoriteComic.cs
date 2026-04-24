using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ComicReader.Models
{
    /// <summary>
    /// Modelo mejorado para un cómic favorito.
    /// Añade tags observables, propiedades calculadas y helpers ligeros.
    /// </summary>
    public class FavoriteComic : INotifyPropertyChanged
    {
        private Guid _id;
        private string _title;
        private string _filePath;
        private string _thumbnail;
        private System.Windows.Media.Imaging.BitmapImage _thumbnailImage;
        private int _currentPage;
        private int _totalPages;
        private DateTime? _lastRead;
        private int _rating;
        private string _author;
        private DateTime _dateAdded;
        private ObservableCollection<string> _tags = new ObservableCollection<string>();
        private string _notes;

        public Guid Id { get => _id; set { _id = value; Raise(); } }
        public string Title { get => _title; set { _title = value; Raise(); } }
        public string FilePath { get => _filePath; set { _filePath = value; Raise(); } }
        public string Thumbnail { get => _thumbnail; set { _thumbnail = value; Raise(); } }
        
        /// <summary>
        /// Imagen de portada del cómic (BitmapImage para binding directo en UI)
        /// </summary>
        public System.Windows.Media.Imaging.BitmapImage ThumbnailImage 
        { 
            get => _thumbnailImage; 
            set { _thumbnailImage = value; Raise(); } 
        }

        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                _currentPage = Math.Max(0, value);
                if (_totalPages > 0) _currentPage = Math.Min(_currentPage, _totalPages);
                Raise();
                Raise(nameof(Progress));
            }
        }

        public int TotalPages
        {
            get => _totalPages;
            set
            {
                _totalPages = Math.Max(0, value);
                if (_currentPage > _totalPages) _currentPage = _totalPages;
                Raise();
                Raise(nameof(Progress));
            }
        }

        public DateTime? LastRead { get => _lastRead; set { _lastRead = value; Raise(); } }

        public int Rating
        {
            get => _rating;
            set { _rating = Math.Max(0, Math.Min(5, value)); Raise(); Raise(nameof(RatingStars)); }
        }

        public string Author { get => _author; set { _author = value; Raise(); } }
        public DateTime DateAdded { get => _dateAdded; set { _dateAdded = value; Raise(); } }

        public ObservableCollection<string> Tags
        {
            get => _tags;
            set
            {
                if (_tags == value) return;
                _tags = value ?? new ObservableCollection<string>();
                Raise();
            }
        }

        public string Notes { get => _notes; set { _notes = value; Raise(); } }

        // Calculated properties
        public string RatingStars => new string('★', Rating) + new string('☆', 5 - Rating);

        public double Progress
        {
            get
            {
                if (_totalPages <= 0) return 0;
                return Math.Round((double)_currentPage / _totalPages * 100.0, 2);
            }
        }

        public string TagsText => Tags != null && Tags.Any() ? string.Join(", ", Tags) : string.Empty;

        public FavoriteComic()
        {
            Id = Guid.NewGuid();
            DateAdded = DateTime.UtcNow;
            Tags.CollectionChanged += (s, e) => { Raise(nameof(TagsText)); };
        }

        public void AddTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;
            if (!Tags.Contains(tag)) Tags.Add(tag);
            Raise(nameof(TagsText));
        }

        public bool RemoveTag(string tag)
        {
            var removed = Tags.Remove(tag);
            if (removed) Raise(nameof(TagsText));
            return removed;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void Raise([CallerMemberName] string propName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
