using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ComicReader.Models;
using System.Linq;

namespace ComicReader.Services
{
    public static class FavoritesStorage
    {
    private static readonly string RootDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PercysLibrary");
        private static readonly string CollectionsFile = Path.Combine(RootDir, "collections.json");

        public static ObservableCollection<ComicCollection> Load()
        {
            try
            {
                if (File.Exists(CollectionsFile))
                {
                    var json = File.ReadAllText(CollectionsFile);
                    var list = JsonSerializer.Deserialize<List<ComicCollection>>(json) ?? new List<ComicCollection>();
                    return new ObservableCollection<ComicCollection>(list);
                }
                else
                {
                    // Fallback: migrar desde carpeta antigua ComicReader si existe
                    var oldRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ComicReader");
                    var oldFile = Path.Combine(oldRoot, "collections.json");
                    if (File.Exists(oldFile))
                    {
                        var json = File.ReadAllText(oldFile);
                        var list = JsonSerializer.Deserialize<List<ComicCollection>>(json) ?? new List<ComicCollection>();
                        try
                        {
                            Directory.CreateDirectory(RootDir);
                            File.Copy(oldFile, CollectionsFile, overwrite: true);
                        }
                        catch (Exception ex) { Logger.LogException("FavoritesStorage.Load - migrate old file copy failed", ex); }
                        return new ObservableCollection<ComicCollection>(list);
                    }
                }
            }
            catch (Exception ex) { Logger.LogException("FavoritesStorage.Load - failed to load collections", ex); }
            return new ObservableCollection<ComicCollection>();
        }

        public static void Save(ObservableCollection<ComicCollection> collections)
        {
            try
            {
                Directory.CreateDirectory(RootDir);
                // Deduplicar por FilePath dentro de cada colección y consolidar progreso básico
                foreach (var col in collections)
                {
                    var unique = new Dictionary<string, FavoriteComic>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in col.Items)
                    {
                        if (string.IsNullOrWhiteSpace(item.FilePath)) continue;
                        if (!unique.TryGetValue(item.FilePath, out var existing))
                        {
                            unique[item.FilePath] = item;
                        }
                        else
                        {
                            // Mantener el progreso y rating más altos
                            if (item.TotalPages > 0 && item.CurrentPage > existing.CurrentPage)
                            {
                                existing.CurrentPage = item.CurrentPage;
                                existing.TotalPages = Math.Max(existing.TotalPages, item.TotalPages);
                                existing.LastRead = item.LastRead ?? existing.LastRead;
                            }
                            existing.Rating = Math.Max(existing.Rating, item.Rating);
                        }
                    }
                    if (unique.Count != col.Items.Count)
                    {
                        col.Items = new System.Collections.ObjectModel.ObservableCollection<FavoriteComic>(unique.Values.OrderBy(c => c.Title));
                    }
                }
                var json = JsonSerializer.Serialize(collections, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(CollectionsFile, json);
            }
            catch (Exception ex) { Logger.LogException("FavoritesStorage.Save - failed to save collections", ex); }
        }

        /// <summary>
        /// Restores a list of items into the specified collection at their original indices when possible.
        /// This is extracted to make undo behavior testable and reusable.
        /// </summary>
        public static void RestoreItems(ComicCollection collection, System.Collections.Generic.IEnumerable<(FavoriteComic Item, int Index)> items)
        {
            if (collection == null || items == null) return;
            // Insert in order by Index so positions are preserved
            foreach (var r in items.OrderBy(i => i.Index))
            {
                try
                {
                    if (r.Item == null) continue;
                    if (r.Index >= 0 && r.Index <= collection.Items.Count)
                        collection.Items.Insert(r.Index, r.Item);
                    else
                        collection.Items.Add(r.Item);
                }
                catch { }
            }
        }
    }
}
