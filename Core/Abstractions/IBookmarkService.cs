using System.Collections.Generic;
using System.Windows.Media.Imaging;
using ComicReader.Models;

namespace ComicReader.Core.Abstractions
{
    public interface IBookmarkService
    {
        void AddBookmark(string comicPath, string comicTitle, int pageNumber, BitmapImage thumbnail, string description = "");
        IEnumerable<BookmarkItem> GetBookmarksForComic(string comicPath);
    }
}
