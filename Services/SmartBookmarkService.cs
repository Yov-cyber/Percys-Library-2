using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using ComicReader.Models;
using ComicReader.Utils;

namespace ComicReader.Services
{
    /// <summary>
    /// Sistema de marcadores inteligentes con vista previa y navegación rápida
    /// </summary>
    public sealed class SmartBookmarkService
    {
        private static readonly Lazy<SmartBookmarkService> _instance = 
            new Lazy<SmartBookmarkService>(() => new SmartBookmarkService());
        
        public static SmartBookmarkService Instance => _instance.Value;

        private readonly string _bookmarksFilePath;
        private Dictionary<string, List<SmartBookmark>> _bookmarks;

        // Eventos
        public event Action<SmartBookmark> BookmarkAdded;
        public event Action<SmartBookmark> BookmarkRemoved;
        public event Action BookmarksChanged;

        private SmartBookmarkService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appData, "PercysLibrary");
            _bookmarksFilePath = Path.Combine(appFolder, "smart-bookmarks.json");

            _bookmarks = new Dictionary<string, List<SmartBookmark>>();
            LoadBookmarks();

            ModernLogger.Info("✓ SmartBookmarkService inicializado");
        }

        /// <summary>
        /// Agrega un marcador inteligente con thumbnail
        /// </summary>
        public async Task<SmartBookmark> AddBookmarkAsync(
            string comicPath, 
            int pageNumber, 
            string note = null,
            byte[] thumbnailData = null)
        {
            try
            {
                var bookmark = new SmartBookmark
                {
                    Id = Guid.NewGuid().ToString(),
                    ComicPath = comicPath,
                    ComicName = Path.GetFileNameWithoutExtension(comicPath),
                    PageNumber = pageNumber,
                    Note = note,
                    CreatedDate = DateTime.Now,
                    ThumbnailData = thumbnailData
                };

                if (!_bookmarks.ContainsKey(comicPath))
                {
                    _bookmarks[comicPath] = new List<SmartBookmark>();
                }

                _bookmarks[comicPath].Add(bookmark);
                await SaveBookmarksAsync();

                ModernLogger.Info($"✓ Marcador agregado: {comicPath} - Página {pageNumber}");
                BookmarkAdded?.Invoke(bookmark);
                BookmarksChanged?.Invoke();

                return bookmark;
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error agregando marcador: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Elimina un marcador
        /// </summary>
        public async Task<bool> RemoveBookmarkAsync(string bookmarkId)
        {
            try
            {
                foreach (var comicBookmarks in _bookmarks.Values)
                {
                    var bookmark = comicBookmarks.FirstOrDefault(b => b.Id == bookmarkId);
                    if (bookmark != null)
                    {
                        comicBookmarks.Remove(bookmark);
                        await SaveBookmarksAsync();

                        ModernLogger.Info($"✓ Marcador eliminado: {bookmarkId}");
                        BookmarkRemoved?.Invoke(bookmark);
                        BookmarksChanged?.Invoke();
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error eliminando marcador: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Obtiene todos los marcadores de un cómic
        /// </summary>
        public List<SmartBookmark> GetBookmarks(string comicPath)
        {
            if (_bookmarks.TryGetValue(comicPath, out var bookmarks))
            {
                return bookmarks.OrderBy(b => b.PageNumber).ToList();
            }
            return new List<SmartBookmark>();
        }

        /// <summary>
        /// Obtiene todos los marcadores
        /// </summary>
        public List<SmartBookmark> GetAllBookmarks()
        {
            return _bookmarks.Values
                .SelectMany(b => b)
                .OrderByDescending(b => b.CreatedDate)
                .ToList();
        }

        /// <summary>
        /// Obtiene el siguiente marcador después de la página actual
        /// </summary>
        public SmartBookmark GetNextBookmark(string comicPath, int currentPage)
        {
            var bookmarks = GetBookmarks(comicPath);
            return bookmarks.FirstOrDefault(b => b.PageNumber > currentPage);
        }

        /// <summary>
        /// Obtiene el marcador anterior antes de la página actual
        /// </summary>
        public SmartBookmark GetPreviousBookmark(string comicPath, int currentPage)
        {
            var bookmarks = GetBookmarks(comicPath);
            return bookmarks.LastOrDefault(b => b.PageNumber < currentPage);
        }

        /// <summary>
        /// Verifica si una página tiene marcador
        /// </summary>
        public bool HasBookmark(string comicPath, int pageNumber)
        {
            var bookmarks = GetBookmarks(comicPath);
            return bookmarks.Any(b => b.PageNumber == pageNumber);
        }

        /// <summary>
        /// Actualiza la nota de un marcador
        /// </summary>
        public async Task<bool> UpdateBookmarkNoteAsync(string bookmarkId, string newNote)
        {
            try
            {
                foreach (var comicBookmarks in _bookmarks.Values)
                {
                    var bookmark = comicBookmarks.FirstOrDefault(b => b.Id == bookmarkId);
                    if (bookmark != null)
                    {
                        bookmark.Note = newNote;
                        bookmark.ModifiedDate = DateTime.Now;
                        await SaveBookmarksAsync();

                        ModernLogger.Info($"✓ Nota de marcador actualizada: {bookmarkId}");
                        BookmarksChanged?.Invoke();
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error actualizando nota: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Busca marcadores por texto
        /// </summary>
        public List<SmartBookmark> SearchBookmarks(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return GetAllBookmarks();

            var search = searchText.ToLowerInvariant();

            return GetAllBookmarks()
                .Where(b => 
                    b.ComicName.ToLowerInvariant().Contains(search) ||
                    (b.Note != null && b.Note.ToLowerInvariant().Contains(search)))
                .ToList();
        }

        /// <summary>
        /// Obtiene estadísticas de marcadores
        /// </summary>
        public BookmarkStatistics GetStatistics()
        {
            var allBookmarks = GetAllBookmarks();

            return new BookmarkStatistics
            {
                TotalBookmarks = allBookmarks.Count,
                ComicsWithBookmarks = _bookmarks.Keys.Count,
                BookmarksWithNotes = allBookmarks.Count(b => !string.IsNullOrEmpty(b.Note)),
                MostBookmarkedComic = _bookmarks
                    .OrderByDescending(kvp => kvp.Value.Count)
                    .FirstOrDefault().Key,
                RecentBookmarks = allBookmarks
                    .OrderByDescending(b => b.CreatedDate)
                    .Take(10)
                    .ToList()
            };
        }

        /// <summary>
        /// Limpia marcadores de cómics que ya no existen
        /// </summary>
        public async Task<int> CleanupOrphanedBookmarksAsync()
        {
            int removed = 0;

            try
            {
                var toRemove = new List<string>();

                foreach (var comicPath in _bookmarks.Keys)
                {
                    if (!File.Exists(comicPath))
                    {
                        toRemove.Add(comicPath);
                        removed += _bookmarks[comicPath].Count;
                    }
                }

                foreach (var path in toRemove)
                {
                    _bookmarks.Remove(path);
                }

                if (removed > 0)
                {
                    await SaveBookmarksAsync();
                    ModernLogger.Info($"🧹 Marcadores huérfanos eliminados: {removed}");
                    BookmarksChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error limpiando marcadores: {ex.Message}");
            }

            return removed;
        }

        // ============================================================
        // PERSISTENCIA
        // ============================================================

        private void LoadBookmarks()
        {
            try
            {
                if (File.Exists(_bookmarksFilePath))
                {
                    var json = File.ReadAllText(_bookmarksFilePath);
                    var bookmarksList = JsonSerializer.Deserialize<List<SmartBookmark>>(json);

                    if (bookmarksList != null)
                    {
                        _bookmarks = bookmarksList
                            .GroupBy(b => b.ComicPath)
                            .ToDictionary(g => g.Key, g => g.ToList());

                        ModernLogger.Info($"✓ Marcadores cargados: {bookmarksList.Count}");
                    }
                }
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error cargando marcadores: {ex.Message}");
            }
        }

        private async Task SaveBookmarksAsync()
        {
            try
            {
                // TODO v3.0: Guardar con PersistenceIntegrator.Instance.AddBookmarkAsync()
                // Por ahora mantener backward compatibility con archivo smart-bookmarks.json
                
                var directory = Path.GetDirectoryName(_bookmarksFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var bookmarksList = _bookmarks.Values.SelectMany(b => b).ToList();
                var json = JsonSerializer.Serialize(bookmarksList, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(_bookmarksFilePath, json);
                ModernLogger.Info($"✓ Marcadores guardados (legacy): {bookmarksList.Count}");
                
                // Nota: El archivo smart-bookmarks.json será eliminado por ConfigurationManager en el próximo inicio
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error guardando marcadores: {ex.Message}");
            }
        }

        // ============================================================
        // CLASES AUXILIARES
        // ============================================================

        public class SmartBookmark
        {
            public string Id { get; set; }
            public string ComicPath { get; set; }
            public string ComicName { get; set; }
            public int PageNumber { get; set; }
            public string Note { get; set; }
            public DateTime CreatedDate { get; set; }
            public DateTime? ModifiedDate { get; set; }
            public byte[] ThumbnailData { get; set; }

            public string DisplayText => 
                $"{ComicName} - Página {PageNumber}" + 
                (string.IsNullOrEmpty(Note) ? "" : $" - {Note}");
        }

        public class BookmarkStatistics
        {
            public int TotalBookmarks { get; set; }
            public int ComicsWithBookmarks { get; set; }
            public int BookmarksWithNotes { get; set; }
            public string MostBookmarkedComic { get; set; }
            public List<SmartBookmark> RecentBookmarks { get; set; }
        }
    }
}
