using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace ComicReader.Models
{
    public class ComicPage : INotifyPropertyChanged
    {
        private int _pageNumber;
        private int _pageIndex;
        private string _fileName;
        private BitmapImage _image;
    private BitmapImage _thumbnail;
        private bool _isCurrent;

        public int PageNumber { get => _pageNumber; set { if (_pageNumber != value) { _pageNumber = value; OnPropertyChanged(); } } }
        public int PageIndex { get => _pageIndex; set { if (_pageIndex != value) { _pageIndex = value; OnPropertyChanged(); } } }
        public string FileName { get => _fileName; set { if (_fileName != value) { _fileName = value; OnPropertyChanged(); } } }
        public BitmapImage Image { get => _image; set { if (_image != value) { _image = value; OnPropertyChanged(); } } }
    public BitmapImage Thumbnail { get => _thumbnail; set { if (_thumbnail != value) { _thumbnail = value; OnPropertyChanged(); } } }
        public bool IsCurrent { get => _isCurrent; set { if (_isCurrent != value) { _isCurrent = value; OnPropertyChanged(); } } }

        public ComicPage() { }

        public ComicPage(int pageNumber, string fileName, BitmapImage image)
        {
            PageNumber = pageNumber;
            PageIndex = pageNumber - 1;
            FileName = fileName;
            Image = image;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
