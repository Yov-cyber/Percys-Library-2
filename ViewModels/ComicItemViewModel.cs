using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ComicReader.ViewModels
{
    public class ComicItemViewModel : INotifyPropertyChanged
    {
        private string _path;
        private string _title;
        private string _thumbPath;
        private bool _isFavorite;
        private string _coverPath;

        public event PropertyChangedEventHandler PropertyChanged;

        protected void Raise([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public string Path { get => _path; set { if (_path != value) { _path = value; Raise(); } } }
        public string Title { get => _title; set { if (_title != value) { _title = value; Raise(); } } }
        public string ThumbPath { get => _thumbPath; set { if (_thumbPath != value) { _thumbPath = value; Raise(); } } }
        public bool IsFavorite { get => _isFavorite; set { if (_isFavorite != value) { _isFavorite = value; Raise(); } } }
        public string CoverPath { get => _coverPath; set { if (_coverPath != value) { _coverPath = value; Raise(); } } }

        public ComicItemViewModel()
        {
            _path = string.Empty;
            _title = string.Empty;
            _thumbPath = string.Empty;
            _coverPath = string.Empty;
            _isFavorite = false;
        }

        public ComicItemViewModel(Core.Abstractions.ComicItemDto dto) : this()
        {
            if (dto == null) return;
            _path = dto.Path ?? string.Empty;
            _title = dto.Title ?? System.IO.Path.GetFileNameWithoutExtension(_path);
            _thumbPath = dto.ThumbPath ?? string.Empty;
            _isFavorite = dto.IsFavorite;
            // ComicItemDto currently does not expose CoverPath in all implementations; leave empty
            _coverPath = string.Empty;
        }

        public Core.Abstractions.ComicItemDto ToDto()
        {
            return new Core.Abstractions.ComicItemDto
            {
                Path = this.Path,
                Title = this.Title,
                ThumbPath = this.ThumbPath,
                IsFavorite = this.IsFavorite
            };
        }
    }
}
