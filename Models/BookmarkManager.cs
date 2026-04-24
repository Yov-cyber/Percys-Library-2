using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using ComicReader.Services;

namespace ComicReader.Models
{
    [Serializable]
    public class BookmarkItem : INotifyPropertyChanged
    {
        private string _title;
        private string _comicFilePath;
        private int _pageNumber;
        private DateTime _dateCreated;
        private string _description;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        public string ComicFilePath
        {
            get => _comicFilePath;
            set { _comicFilePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(ComicFileName)); }
        }

        public int PageNumber
        {
            get => _pageNumber;
            set { _pageNumber = value; OnPropertyChanged(); }
        }

        public DateTime DateCreated
        {
            get => _dateCreated;
            set { _dateCreated = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public string ComicFileName => Path.GetFileNameWithoutExtension(ComicFilePath);

        public BookmarkItem()
        {
            DateCreated = DateTime.Now;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class BookmarkManager : INotifyPropertyChanged
    {
        private static BookmarkManager _instance;
        public static BookmarkManager Instance => _instance ??= new BookmarkManager();

        public event PropertyChangedEventHandler PropertyChanged;

        private ObservableCollection<BookmarkItem> _bookmarks = new ObservableCollection<BookmarkItem>();

        public ObservableCollection<BookmarkItem> Bookmarks
        {
            get => _bookmarks;
            set { _bookmarks = value; OnPropertyChanged(); }
        }

        private readonly string _bookmarksPath;

        public BookmarkManager()
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PercysLibrary");
            Directory.CreateDirectory(appDataPath);
            _bookmarksPath = Path.Combine(appDataPath, "bookmarks.xml");
            
            LoadBookmarks();
        }

        public void AddBookmark(string comicFilePath, int pageNumber, string title, string description = "")
        {
            var existing = Bookmarks.FirstOrDefault(b => b.ComicFilePath == comicFilePath && b.PageNumber == pageNumber);
            if (existing != null)
            {
                existing.Title = title;
                existing.Description = description;
            }
            else
            {
                var bookmark = new BookmarkItem
                {
                    ComicFilePath = comicFilePath,
                    PageNumber = pageNumber,
                    Title = title,
                    Description = description
                };
                Bookmarks.Insert(0, bookmark);
            }
            
            SaveBookmarks();
        }

        public void AddBookmark(string comicFilePath, string comicTitle, int pageNumber, object thumbnail, string description = "")
        {
            // Versión sobrecargada que acepta thumbnail (ignoramos el thumbnail por ahora)
            AddBookmark(comicFilePath, pageNumber, comicTitle, description);
        }

        public void RemoveBookmark(BookmarkItem bookmark)
        {
            Bookmarks.Remove(bookmark);
            SaveBookmarks();
        }

        public List<BookmarkItem> GetBookmarksForComic(string comicFilePath)
        {
            return Bookmarks.Where(b => b.ComicFilePath.Equals(comicFilePath, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private void LoadBookmarks()
        {
            try
            {
                if (File.Exists(_bookmarksPath))
                {
                    var serializer = new XmlSerializer(typeof(BookmarkItem[]));
                    using (var reader = new FileStream(_bookmarksPath, FileMode.Open))
                    {
                        if (serializer.Deserialize(reader) is BookmarkItem[] bookmarks)
                        {
                            Bookmarks = new ObservableCollection<BookmarkItem>(bookmarks);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error loading bookmarks: {ex.Message}");
                Bookmarks = new ObservableCollection<BookmarkItem>();
            }
        }

        private void SaveBookmarks()
        {
            try
            {
                var serializer = new XmlSerializer(typeof(BookmarkItem[]));
                using (var writer = new FileStream(_bookmarksPath, FileMode.Create))
                {
                    serializer.Serialize(writer, Bookmarks.ToArray());
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error saving bookmarks: {ex.Message}");
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}