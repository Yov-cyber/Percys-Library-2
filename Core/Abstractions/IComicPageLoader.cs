using System.Collections.Generic;
using System.Threading.Tasks;
using ComicReader.Models;

namespace ComicReader.Core.Abstractions
{
    public interface IComicPageLoader
    {
        List<ComicPage> Pages { get; }
        string FilePath { get; }
        string ComicTitle { get; }
        Task LoadComicAsync(string filePath = null);
    System.Threading.Tasks.Task<System.Windows.Media.Imaging.BitmapImage> GetPageImageAsync(int pageNumber, int targetWidth = 0);
        // Event: se dispara cuando la versión full de una página ya está disponible
        event System.Action<int, System.Windows.Media.Imaging.BitmapImage> FullImageReady;
    }
}
