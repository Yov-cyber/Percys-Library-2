using System.Windows.Media.Imaging;
using System.Threading.Tasks;

namespace ComicReader.Core.Abstractions
{
    public interface IImageCache
    {
        Task<BitmapImage> Get(string key);
        Task Set(string key, BitmapImage image);
        void PurgeMemory();
    }
}
