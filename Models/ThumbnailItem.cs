using System.ComponentModel;
using System.Windows.Media.Imaging;

namespace ComicReader.Models
{
    public class ThumbnailItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private BitmapImage _thumbnail;
        private bool _isCurrentPage;
        private bool _hasBookmarks;
        private int _bookmarkCount;
        private string _bookmarkNotes;
        private bool _showBookmarkIndicator;
        private bool _isLoading = true;

        public int PageIndex { get; set; }
        public int PageNumber { get; set; }

        public BitmapImage Thumbnail
        {
            get => _thumbnail;
            set
            {
                _thumbnail = value;
                OnPropertyChanged(nameof(Thumbnail));
                IsLoading = false;
            }
        }

        public bool IsCurrentPage
        {
            get => _isCurrentPage;
            set { _isCurrentPage = value; OnPropertyChanged(nameof(IsCurrentPage)); }
        }

        public bool HasBookmarks
        {
            get => _hasBookmarks;
            set { _hasBookmarks = value; OnPropertyChanged(nameof(HasBookmarks)); }
        }

        public int BookmarkCount
        {
            get => _bookmarkCount;
            set { _bookmarkCount = value; OnPropertyChanged(nameof(BookmarkCount)); }
        }

        public string BookmarkNotes
        {
            get => _bookmarkNotes;
            set { _bookmarkNotes = value; OnPropertyChanged(nameof(BookmarkNotes)); }
        }

        public bool ShowBookmarkIndicator
        {
            get => _showBookmarkIndicator;
            set { _showBookmarkIndicator = value; OnPropertyChanged(nameof(ShowBookmarkIndicator)); }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(nameof(IsLoading)); OnPropertyChanged(nameof(IsNotLoading)); }
        }

        public bool IsNotLoading => !IsLoading;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}