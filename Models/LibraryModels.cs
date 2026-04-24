using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace ComicReader.Models
{
    public class ComicLibrary : INotifyPropertyChanged
    {
        private Guid _id;
        private string _name;
        private string _path;
        private string _description;
        private DateTime _dateCreated;
        private int _comicCount;
        private string _category;
        private bool _isDefault;
        private ObservableCollection<string> _tags;

        public Guid Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Path
        {
            get => _path;
            set { _path = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public DateTime DateCreated
        {
            get => _dateCreated;
            set { _dateCreated = value; OnPropertyChanged(); }
        }

        public int ComicCount
        {
            get => _comicCount;
            set { _comicCount = value; OnPropertyChanged(); }
        }

        public string Category
        {
            get => _category;
            set { _category = value; OnPropertyChanged(); }
        }

        public bool IsDefault
        {
            get => _isDefault;
            set { _isDefault = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> Tags
        {
            get => _tags ??= new ObservableCollection<string>();
            set { _tags = value; OnPropertyChanged(); }
        }

        public string StatusIcon => IsDefault ? "🏠" : "📚";
        public string ComicCountText => $"{ComicCount} cómic{(ComicCount != 1 ? "s" : "")}";

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ComicItem : INotifyPropertyChanged
    {
        private Guid _id;
        private string _title;
        private string _filePath;
        private long _fileSize;
        private DateTime _dateAdded;
        private DateTime _lastRead;
        private string _format;
        private bool _isRead;
        private int _rating;
        private ObservableCollection<string> _tags;
        private string _series;
        private int _issueNumber;
        private string _author;
        private string _genre;

        public Guid Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        public string FilePath
        {
            get => _filePath;
            set { _filePath = value; OnPropertyChanged(); }
        }

        public long FileSize
        {
            get => _fileSize;
            set { _fileSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(FileSizeFormatted)); }
        }

        public DateTime DateAdded
        {
            get => _dateAdded;
            set { _dateAdded = value; OnPropertyChanged(); }
        }

        public DateTime LastRead
        {
            get => _lastRead;
            set { _lastRead = value; OnPropertyChanged(); }
        }

        public string Format
        {
            get => _format;
            set { _format = value; OnPropertyChanged(); }
        }

        public bool IsRead
        {
            get => _isRead;
            set { _isRead = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReadStatusIcon)); }
        }

        public int Rating
        {
            get => _rating;
            set { _rating = value; OnPropertyChanged(); OnPropertyChanged(nameof(RatingStars)); }
        }

        public ObservableCollection<string> Tags
        {
            get => _tags ??= new ObservableCollection<string>();
            set { _tags = value; OnPropertyChanged(); }
        }

        public string Series
        {
            get => _series;
            set { _series = value; OnPropertyChanged(); }
        }

        public int IssueNumber
        {
            get => _issueNumber;
            set { _issueNumber = value; OnPropertyChanged(); }
        }

        public string Author
        {
            get => _author;
            set { _author = value; OnPropertyChanged(); }
        }

        public string Genre
        {
            get => _genre;
            set { _genre = value; OnPropertyChanged(); }
        }

        public string FileSizeFormatted
        {
            get
            {
                string[] suffixes = { "B", "KB", "MB", "GB" };
                int counter = 0;
                decimal number = FileSize;
                while (Math.Round(number / 1024) >= 1)
                {
                    number /= 1024;
                    counter++;
                }
                return $"{number:n1} {suffixes[counter]}";
            }
        }

        public string ReadStatusIcon => IsRead ? "✅" : "⭕";
        public string RatingStars => new string('⭐', Math.Max(0, Math.Min(5, Rating)));

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}