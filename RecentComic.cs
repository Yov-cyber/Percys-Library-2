// FileName: /Models/RecentComic.cs
using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Media.Imaging;

namespace ComicReader.Models
{
    public class RecentComic : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _filePath;
        public string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    OnPropertyChanged(nameof(FilePath));
                    OnPropertyChanged(nameof(Title));
                }
            }
        }

        private int _lastPage;
        public int LastPage
        {
            get => _lastPage;
            set
            {
                if (_lastPage != value)
                {
                    _lastPage = value;
                    OnPropertyChanged(nameof(LastPage));
                }
            }
        }

        private DateTime _lastOpened;
        public DateTime LastOpened
        {
            get => _lastOpened;
            set
            {
                if (_lastOpened != value)
                {
                    _lastOpened = value;
                    OnPropertyChanged(nameof(LastOpened));
                }
            }
        }

        public string Title => string.IsNullOrEmpty(FilePath) ? "Comic sin título" : Path.GetFileNameWithoutExtension(FilePath);

        [System.Xml.Serialization.XmlIgnore]
        private BitmapImage _coverThumbnail;
        [System.Xml.Serialization.XmlIgnore]
        public BitmapImage CoverThumbnail
        {
            get => _coverThumbnail;
            set
            {
                _coverThumbnail = value;
                OnPropertyChanged(nameof(CoverThumbnail));
            }
        }
    }
}