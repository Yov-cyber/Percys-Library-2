using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ComicReader.Commands;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComicReader.Models;
using ComicReader.Services;
using ComicReader.Core.Services;
using ComicReader.Core.Abstractions;
using ComicReader.Core.Adapters;

namespace ComicReader.ViewModels
{
    public class ThumbnailGridViewViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
    public event System.Action<int> PageSelected;
    public event System.Action<int> PageDoubleClicked;

        private ObservableCollection<ThumbnailItem> _thumbnails = new();
    private IComicPageLoader _comicLoader;
    private readonly IBookmarkService _bookmarkService;
        private int _currentPageIndex;
        private int _thumbnailSize = 150;
        private bool _isLoading;
        private bool _showPageNumbers = true;
        private bool _showBookmarks = true;
        private CancellationTokenSource _thumbnailCts;
    private readonly Dictionary<int, BitmapImage> _fullImageCache = new(); // raw original page images
    private readonly Dictionary<(int page, int size), BitmapImage> _thumbnailCache = new(); // sized variants (fast reuse)
    private readonly IImageCache _multiLevelCache;
    private readonly ILogService _log;

        public ObservableCollection<ThumbnailItem> Thumbnails
        {
            get => _thumbnails;
            private set { _thumbnails = value; OnPropertyChanged(nameof(Thumbnails)); }
        }

        public IComicPageLoader ComicLoader
        {
            get => _comicLoader;
            set
            {
                _comicLoader = value;
                OnPropertyChanged(nameof(ComicLoader));
                StartThumbnailLoad();
            }
        }

        public int CurrentPageIndex
        {
            get => _currentPageIndex;
            set
            {
                if (_currentPageIndex != value)
                {
                    _currentPageIndex = value;
                    OnPropertyChanged(nameof(CurrentPageIndex));
                    UpdateCurrentSelection();
                }
            }
        }

        public int ThumbnailSize
        {
            get => _thumbnailSize;
            set
            {
                var clamped = Math.Max(100, Math.Min(300, value));
                if (_thumbnailSize != clamped)
                {
                    _thumbnailSize = clamped;
                    OnPropertyChanged(nameof(ThumbnailSize));
                    UpdateThumbnailSizes();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set { _isLoading = value; OnPropertyChanged(nameof(IsLoading)); OnPropertyChanged(nameof(IsNotLoading)); }
        }

        public bool IsNotLoading => !IsLoading;

        public bool ShowPageNumbers
        {
            get => _showPageNumbers;
            set { _showPageNumbers = value; OnPropertyChanged(nameof(ShowPageNumbers)); }
        }

        public bool ShowBookmarks
        {
            get => _showBookmarks;
            set { _showBookmarks = value; OnPropertyChanged(nameof(ShowBookmarks)); UpdateBookmarkVisibility(); }
        }

        public ICommand IncreaseThumbSizeCommand { get; }
        public ICommand DecreaseThumbSizeCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ThumbnailClickCommand { get; }
        public ICommand ThumbnailDoubleClickCommand { get; }
        public ICommand AddBookmarkCommand { get; }

        public ThumbnailGridViewViewModel()
        {
            _bookmarkService = ServiceLocator.Get<IBookmarkService>();
            _log = ServiceLocator.TryGet<ILogService>() ?? new LogServiceAdapter();
            _multiLevelCache = ServiceLocator.TryGet<IImageCache>();
            // Usar overloads explícitos para evitar ambigüedad con System.Action
            IncreaseThumbSizeCommand = new RelayCommand((Action)(() => ThumbnailSize += 25));
            DecreaseThumbSizeCommand = new RelayCommand((Action)(() => ThumbnailSize -= 25));
            RefreshCommand = new RelayCommand((Action)(() => StartThumbnailLoad(true)));
            ThumbnailClickCommand = new RelayCommand((object t) => { if (t is ThumbnailItem ti) ThumbnailClicked(ti); });
            ThumbnailDoubleClickCommand = new RelayCommand((object t) => { if (t is ThumbnailItem ti) ThumbnailDoubleClicked(ti); });
            AddBookmarkCommand = new RelayCommand((Action)(() => AddBookmarkToCurrentPage()));
        }

        private void StartThumbnailLoad(bool force = false)
        {
            if (_comicLoader?.Pages == null) return;
            _thumbnailCts?.Cancel();
            _thumbnailCts = new CancellationTokenSource();
            LoadThumbnailsAsync(_thumbnailCts.Token);
        }

        private async void LoadThumbnailsAsync(CancellationToken ct)
        {
            if (ComicLoader?.Pages == null) return;

            IsLoading = true;
            Thumbnails.Clear();

            try
            {
                var thumbnailTasks = ComicLoader.Pages.Select(async (page, index) =>
                {
                    ct.ThrowIfCancellationRequested();
                    var thumbnail = await GetOrCreateThumbnailAsync(index, ThumbnailSize, ct);
                    var bookmarks = _bookmarkService.GetBookmarksForComic(ComicLoader.FilePath)
                                                             .Where(b => b.PageNumber == index + 1)
                                                             .ToList();

                    return new ThumbnailItem
                    {
                        PageIndex = index,
                        PageNumber = index + 1,
                        Thumbnail = thumbnail,
                        IsCurrentPage = index == CurrentPageIndex,
                        HasBookmarks = bookmarks.Any(),
                        BookmarkCount = bookmarks.Count,
                        BookmarkNotes = string.Join("; ", bookmarks.Where(b => !string.IsNullOrWhiteSpace(b.Description)).Select(b => b.Description))
                    };
                });

                var thumbnailResults = await Task.WhenAll(thumbnailTasks);
                foreach (var thumb in thumbnailResults)
                    Thumbnails.Add(thumb);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _log?.Log($"Error loading thumbnails: {ex.Message}", LogLevel.Warning);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task<BitmapImage> GetOrCreateThumbnailAsync(int pageIndex, int size, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var cacheKey = $"thumb_{ComicLoader.FilePath}_{pageIndex}_{size}";
                if (_thumbnailCache.TryGetValue((pageIndex, size), out var cachedThumb))
                    return cachedThumb;
                // multi-level cache lookup
                if (_multiLevelCache != null)
                {
                    var diskCached = await _multiLevelCache.Get(cacheKey);
                    if (diskCached != null)
                    {
                        _thumbnailCache[(pageIndex, size)] = diskCached;
                        return diskCached;
                    }
                }

                if (!_fullImageCache.TryGetValue(pageIndex, out var fullImage))
                {
                    // Request the full image decoded to a reasonable width to reduce CPU and memory
                    if (ComicLoader is ComicPageLoader cpl)
                        fullImage = await cpl.GetPageImageAsync(pageIndex, 1200, ct).ConfigureAwait(false);
                    else
                        fullImage = await ((IComicPageLoader)ComicLoader).GetPageImageAsync(pageIndex, 1200).ConfigureAwait(false);

                    if (fullImage == null) return null;
                    _fullImageCache[pageIndex] = fullImage;
                }

                var thumb = await Task.Run(() => CreateThumbnail(fullImage, size), ct);
                if (thumb != null)
                {
                    _thumbnailCache[(pageIndex, size)] = thumb;
                    if (_multiLevelCache != null)
                        await _multiLevelCache.Set(cacheKey, thumb);
                }
                return thumb;
            }
            catch (Exception ex)
            {
                _log?.Log($"Error loading thumbnail for page {pageIndex}: {ex.Message}", LogLevel.Warning);
                return CreateErrorThumbnail();
            }
        }

        private BitmapImage CreateThumbnail(BitmapImage source, int maxSize)
        {
            if (source == null) return null;
            try
            {
                var scaleX = (double)maxSize / source.PixelWidth;
                var scaleY = (double)maxSize / source.PixelHeight;
                var scale = Math.Min(scaleX, scaleY);
                var width = (int)(source.PixelWidth * scale);
                var height = (int)(source.PixelHeight * scale);
                // Create a scaled bitmap from the already-loaded source. Use TransformedBitmap which can be frozen.
                var frame = BitmapFrame.Create(source);
                var scaleTransform = new System.Windows.Media.ScaleTransform(scale, scale);
                var tb = new TransformedBitmap(frame, scaleTransform);
                tb.Freeze();
                // Convert to BitmapImage by encoding to PNG in-memory
                using (var ms = new System.IO.MemoryStream())
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(tb));
                    encoder.Save(ms);
                    ms.Position = 0;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
            }
            catch { return CreateErrorThumbnail(); }
        }

        private BitmapImage CreateErrorThumbnail()
        {
            var errorBitmap = new WriteableBitmap(ThumbnailSize, ThumbnailSize, 96, 96, PixelFormats.Bgr24, null);
            var stride = errorBitmap.PixelWidth * 3;
            var pixels = new byte[stride * errorBitmap.PixelHeight];
            for (int i = 0; i < pixels.Length; i += 3)
            { pixels[i] = 0; pixels[i + 1] = 0; pixels[i + 2] = 128; }
            errorBitmap.WritePixels(new System.Windows.Int32Rect(0, 0, errorBitmap.PixelWidth, errorBitmap.PixelHeight), pixels, stride, 0);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(errorBitmap));
            using var stream = new System.IO.MemoryStream();
            encoder.Save(stream);
            stream.Position = 0;
            var result = new BitmapImage();
            result.BeginInit();
            result.StreamSource = stream;
            result.CacheOption = BitmapCacheOption.OnLoad;
            result.EndInit();
            result.Freeze();
            return result;
        }

        private void UpdateCurrentSelection()
        {
            foreach (var t in Thumbnails)
                t.IsCurrentPage = t.PageIndex == CurrentPageIndex;
        }

        private void UpdateThumbnailSizes()
        {
            if (Thumbnails.Count == 0)
            {
                StartThumbnailLoad(true);
                return;
            }
            foreach (var item in Thumbnails)
            {
                if (_fullImageCache.TryGetValue(item.PageIndex, out var full))
                {
                    var key = (item.PageIndex, ThumbnailSize);
                    if (!_thumbnailCache.TryGetValue(key, out var resized))
                    {
                        try
                        {
                            resized = CreateThumbnail(full, ThumbnailSize);
                            if (resized != null)
                                _thumbnailCache[key] = resized;
                        }
                        catch { }
                    }
                    if (resized != null)
                        item.Thumbnail = resized;
                }
            }
        }

        private void UpdateBookmarkVisibility()
        {
            foreach (var t in Thumbnails)
                t.ShowBookmarkIndicator = ShowBookmarks && t.HasBookmarks;
        }

        private void ThumbnailClicked(ThumbnailItem thumbnail)
        {
            CurrentPageIndex = thumbnail.PageIndex;
            PageSelected?.Invoke(thumbnail.PageIndex);
        }

        private void ThumbnailDoubleClicked(ThumbnailItem thumbnail)
        {
            PageDoubleClicked?.Invoke(thumbnail.PageIndex);
        }

        public void AddBookmarkToCurrentPage(string note = "")
        {
            if (ComicLoader?.Pages != null && CurrentPageIndex >= 0 && CurrentPageIndex < ComicLoader.Pages.Count)
            {
                var currentThumbnail = Thumbnails.FirstOrDefault(t => t.PageIndex == CurrentPageIndex);
                var thumbnailImage = currentThumbnail?.Thumbnail;
                _bookmarkService.AddBookmark(ComicLoader.FilePath, ComicLoader.ComicTitle, CurrentPageIndex + 1, thumbnailImage, note);
                if (currentThumbnail != null)
                {
                    var bookmarks = _bookmarkService.GetBookmarksForComic(ComicLoader.FilePath)
                                                           .Where(b => b.PageNumber == CurrentPageIndex + 1)
                                                           .ToList();
                    currentThumbnail.HasBookmarks = bookmarks.Any();
                    currentThumbnail.BookmarkCount = bookmarks.Count;
                    currentThumbnail.BookmarkNotes = string.Join("; ", bookmarks.Where(b => !string.IsNullOrWhiteSpace(b.Description)).Select(b => b.Description));
                    currentThumbnail.ShowBookmarkIndicator = ShowBookmarks && currentThumbnail.HasBookmarks;
                }
            }
        }

        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
