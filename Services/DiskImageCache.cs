using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Linq;

namespace ComicReader.Services
{
    /// <summary>
    /// Simple disk cache for images keyed by a string. Stores files under LocalAppData\PercysLibrary\cache
    /// with an LRU-like cleanup by access timestamp.
    /// </summary>
    public static class DiskImageCache
    {
        private static readonly string CacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PercysLibrary", "imagecache");
        static DiskImageCache()
        {
            try { Directory.CreateDirectory(CacheDir); } catch { }
        }

        private static string KeyToName(string key)
        {
            using (var sha = SHA1.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(key);
                var hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", string.Empty) + ".png";
            }
        }

        public static string GetPathForKey(string key) => Path.Combine(CacheDir, KeyToName(key));

        public static bool TryGet(string key, out string path)
        {
            path = GetPathForKey(key);
            if (File.Exists(path))
            {
                try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); } catch { }
                return true;
            }
            path = null;
            return false;
        }

        public static async Task SaveAsync(string key, byte[] data)
        {
            var path = GetPathForKey(key);
            try
            {
                await File.WriteAllBytesAsync(path, data).ConfigureAwait(false);
            }
            catch { }
        }

        public static void CleanupOld(int maxFiles)
        {
            try
            {
                var files = new DirectoryInfo(CacheDir).GetFiles().OrderBy(f => f.LastAccessTimeUtc).ToList();
                if (maxFiles > 0)
                {
                    int remove = Math.Max(0, files.Count - maxFiles);
                    for (int i = 0; i < remove; i++)
                    {
                        try { files[i].Delete(); } catch { }
                    }
                }
            }
            catch { }
        }

        public static void CleanupOldByBytes(long maxBytes)
        {
            try
            {
                if (maxBytes <= 0) return;
                var files = new DirectoryInfo(CacheDir).GetFiles().OrderBy(f => f.LastAccessTimeUtc).ToList();
                long total = files.Sum(f => f.Length);
                int idx = 0;
                while (total > maxBytes && idx < files.Count)
                {
                    try
                    {
                        var fi = files[idx];
                        long len = fi.Length;
                        fi.Delete();
                        total -= len;
                    }
                    catch { }
                    idx++;
                }
            }
            catch { }
        }
    }
}
