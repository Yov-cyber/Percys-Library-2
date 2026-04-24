using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;
using ComicReader.Core.Abstractions;
using ComicReader.Models;

namespace ComicReader.Core.Adapters
{
    public class BookmarkServiceAdapter : IBookmarkService
    {
        public void AddBookmark(string comicPath, string comicTitle, int pageNumber, BitmapImage thumbnail, string description = "")
        {
            BookmarkManager.Instance.AddBookmark(comicPath, comicTitle, pageNumber, thumbnail, description);
        }

        public IEnumerable<BookmarkItem> GetBookmarksForComic(string comicPath)
        {
            return BookmarkManager.Instance.GetBookmarksForComic(comicPath);
        }
    }
}
