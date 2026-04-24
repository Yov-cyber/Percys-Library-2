using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Tar;
using SharpCompress.Readers;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Media.Imaging;

namespace ComicReader.Services
{
    public static class ComicFileLoader
    {
        public static List<BitmapImage> LoadComic(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLower();
            if (ext == ".cbz" || ext == ".zip")
                return LoadFromArchive(filePath, ArchiveType.Zip);
            if (ext == ".cbr" || ext == ".rar")
                return LoadFromArchive(filePath, ArchiveType.Rar);
            if (ext == ".cb7" || ext == ".7z")
                return LoadFromArchive(filePath, ArchiveType.SevenZip);
            if (ext == ".cbt" || ext == ".tar")
                return LoadFromArchive(filePath, ArchiveType.Tar);
            if (ext == ".pdf")
                return LoadFromPdf(filePath);
            if (ext == ".epub")
                return LoadFromEpub(filePath);
            if (ext == ".djvu")
                return LoadFromDjvu(filePath);
            if (IsImage(ext))
                return new List<BitmapImage> { LoadImage(filePath) };
            if (Directory.Exists(filePath))
                return LoadFromFolder(filePath);
            throw new NotSupportedException($"Formato no soportado: {ext}");
        }
        private static List<BitmapImage> LoadFromFolder(string folderPath)
        {
            var images = new List<BitmapImage>();
            var files = Directory.GetFiles(folderPath)
                .Where(f => IsImage(Path.GetExtension(f).ToLower()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                images.Add(LoadImage(file));
            }
            return images;
        }

        private static List<BitmapImage> LoadFromArchive(string filePath, ArchiveType type)
        {
            var images = new List<BitmapImage>();
            IArchive archive = type switch
            {
                ArchiveType.Zip => ZipArchive.Open(filePath),
                ArchiveType.Rar => RarArchive.Open(filePath),
                ArchiveType.SevenZip => SevenZipArchive.Open(filePath),
                ArchiveType.Tar => TarArchive.Open(filePath),
                _ => throw new NotSupportedException()
            };
            foreach (var entry in archive.Entries.Where(e => !e.IsDirectory && IsImage(Path.GetExtension(e.Key).ToLower())))
            {
                using var ms = new MemoryStream();
                entry.WriteTo(ms);
                ms.Seek(0, SeekOrigin.Begin);
                images.Add(LoadImage(ms));
            }
            return images;
        }

        private static BitmapImage LoadImage(string path)
        {
            // Open the file as stream and delegate to the stream-based loader to control disposal and ensure OnLoad
            using (var fs = File.OpenRead(path))
            {
                return LoadImage(fs);
            }
        }
        private static BitmapImage LoadImage(Stream stream)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        private static bool IsImage(string ext)
        {
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".gif" || ext == ".bmp" || ext == ".webp" || ext == ".tiff" || ext == ".heic" || ext == ".avif";
        }
        private static List<BitmapImage> LoadFromPdf(string filePath)
        {
            // TODO: Integrar PdfiumViewer o PdfSharp para extraer páginas como imágenes
            return new List<BitmapImage>();
        }
        private static List<BitmapImage> LoadFromEpub(string filePath)
        {
            // TODO: Integrar VersOne.Epub para extraer imágenes
            return new List<BitmapImage>();
        }
        private static List<BitmapImage> LoadFromDjvu(string filePath)
        {
            // TODO: Integrar DjvuNet para extraer páginas
            return new List<BitmapImage>();
        }
    }
    public enum ArchiveType { Zip, Rar, SevenZip, Tar }
}
