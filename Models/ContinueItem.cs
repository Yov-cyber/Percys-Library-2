using System;
using System.IO;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace ComicReader.Models
{
    public class ContinueItem : INotifyPropertyChanged
    {
        private string _filePath;
        private string _displayName;
        private int _lastPage;
        private int _pageCount;
        private DateTime _lastOpened;
        private bool _isCompleted;
    private DateTime? _dateCompleted;
    private int? _totalReadSeconds;
    private int? _rating;
    private string _review;

        [JsonPropertyName("filePath")]
        public string FilePath
        {
            get => _filePath;
            set { if (_filePath != value) { _filePath = value; OnPropertyChanged(); } }
        }

        [JsonPropertyName("displayName")]
        public string DisplayName
        {
            get => _displayName;
            set { if (_displayName != value) { _displayName = value; OnPropertyChanged(); } }
        }

        [JsonPropertyName("lastPage")]
        public int LastPage
        {
            get => _lastPage;
            set { if (_lastPage != value) { _lastPage = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); } }
        }

        [JsonPropertyName("pageCount")]
        public int PageCount
        {
            get => _pageCount;
            set { if (_pageCount != value) { _pageCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); } }
        }

        [JsonPropertyName("lastOpened")]
        public DateTime LastOpened
        {
            get => _lastOpened;
            set { if (_lastOpened != value) { _lastOpened = value; OnPropertyChanged(); } }
        }

        [JsonPropertyName("isCompleted")]
        public bool IsCompleted
        {
            get => _isCompleted;
            set { if (_isCompleted != value) { _isCompleted = value; OnPropertyChanged(); } }
        }

        [JsonPropertyName("dateCompleted")]
        public DateTime? DateCompleted
        {
            get => _dateCompleted;
            set { if (_dateCompleted != value) { _dateCompleted = value; OnPropertyChanged(); } }
        }

        [JsonPropertyName("totalReadSeconds")]
        public int? TotalReadSeconds
        {
            get => _totalReadSeconds;
            set { if (_totalReadSeconds != value) { _totalReadSeconds = value; OnPropertyChanged(); } }
        }

        [JsonPropertyName("rating")]
        public int? Rating
        {
            get => _rating;
            set { if (_rating != value) { _rating = value; OnPropertyChanged(); } }
        }

        [JsonPropertyName("review")]
        public string Review
        {
            get => _review;
            set { if (_review != value) { _review = value; OnPropertyChanged(); } }
        }

        [JsonIgnore]
        public double ProgressPercent => PageCount <= 0 ? 0 : Math.Round(100.0 * Math.Max(0, Math.Min(PageCount, LastPage)) / PageCount, 1);

        [JsonIgnore]
        private BitmapImage _coverThumbnail;
        [JsonIgnore]
        public BitmapImage CoverThumbnail
        {
            get => _coverThumbnail;
            set
            {
                if (Equals(_coverThumbnail, value)) return;
                _coverThumbnail = value;
                // Notificar solamente una vez para evitar que listeners reasignen CoverPath -> reentradas
                OnPropertyChanged(nameof(CoverThumbnail));
            }
        }

        private string _coverPath;
        [JsonPropertyName("coverPath")]
        public string CoverPath
        {
            get => _coverPath;
            set
            {
                if (_coverPath == value) return;
                _coverPath = value;
                OnPropertyChanged(nameof(CoverPath));

                // Cargar la imagen directamente en el backing field para evitar disparar lógica
                // en el setter de CoverThumbnail que pudiera reinvocarse y crear un bucle.
                try
                {
                    if (!string.IsNullOrWhiteSpace(_coverPath) && File.Exists(_coverPath))
                    {
                        // NOTE: previously this setter started an untracked background load of the image file.
                        // Loading of cover thumbnails is now the responsibility of the UI layer (HomeView/ContinueReadingService)
                        // which already performs tracked background loads. This avoids orphaned Task.Run calls.
                    }
                }
                catch { }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
