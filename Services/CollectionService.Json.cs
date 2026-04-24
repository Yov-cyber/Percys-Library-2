using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ComicReader.Core.Abstractions;

namespace ComicReader.Services
{
    public class CollectionServiceJson : ICollectionService
    {
        private readonly string _filePath;
        private readonly object _sync = new object();
        private List<CollectionDto> _cache = new List<CollectionDto>();

        public CollectionServiceJson()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "PercysLibrary");
            try { Directory.CreateDirectory(dir); } catch { }
            _filePath = Path.Combine(dir, "collections.json");
            Load();
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var txt = File.ReadAllText(_filePath);
                    var arr = JsonSerializer.Deserialize<List<CollectionDto>>(txt);
                    if (arr != null) _cache = arr;
                }
            }
            catch { }
        }

        private void Save()
        {
            try
            {
                var txt = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, txt);
            }
            catch { }
        }

        public CollectionDto Create(CollectionCreateRequest req)
        {
            var c = new CollectionDto { Name = req.Name, Description = req.Description, CoverPath = req.CoverPath };
            lock (_sync) { _cache.Add(c); Save(); }
            return c;
        }

        public void Delete(Guid id)
        {
            lock (_sync) { _cache.RemoveAll(x => x.Id == id); Save(); }
        }

        public void AddRaw(CollectionDto dto, int? index = null)
        {
            if (dto == null) return;
            lock (_sync)
            {
                // avoid duplicate ids
                if (_cache.Any(x => x.Id == dto.Id)) return;
                if (index.HasValue && index.Value >= 0 && index.Value <= _cache.Count)
                {
                    _cache.Insert(index.Value, dto);
                }
                else
                {
                    _cache.Add(dto);
                }
                Save();
            }
        }

        public IEnumerable<CollectionDto> GetAll()
        {
            lock (_sync) return _cache.Select(x => x).ToList();
        }

        public CollectionDto Duplicate(Guid id)
        {
            lock (_sync)
            {
                var orig = _cache.FirstOrDefault(x => x.Id == id);
                if (orig == null) return null;
                var copy = new CollectionDto
                {
                    Name = orig.Name + " (copia)",
                    Description = orig.Description,
                    CoverPath = orig.CoverPath,
                    Items = orig.Items?.Select(i => new ComicItemDto { Path = i.Path, Title = i.Title, ThumbPath = i.ThumbPath }).ToList() ?? new List<ComicItemDto>(),
                    Count = orig.Count
                };
                _cache.Add(copy);
                Save();
                return copy;
            }
        }

        public void ExportCollections(string path)
        {
            try { File.WriteAllText(path, JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true })); } catch { }
        }

        public void ImportCollections(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                var txt = File.ReadAllText(path);
                var arr = JsonSerializer.Deserialize<List<CollectionDto>>(txt);
                if (arr == null) return;
                lock (_sync) { _cache.AddRange(arr); Save(); }
            }
            catch { }
        }

        public CollectionDto Rename(Guid id, string newName)
        {
            lock (_sync)
            {
                var it = _cache.FirstOrDefault(x => x.Id == id);
                if (it == null) return null;
                it.Name = newName;
                Save();
                return it;
            }
        }

        public CollectionDto Update(Guid id, CollectionCreateRequest req)
        {
            lock (_sync)
            {
                var it = _cache.FirstOrDefault(x => x.Id == id);
                if (it == null) return null;
                if (!string.IsNullOrWhiteSpace(req.Name)) it.Name = req.Name;
                it.Description = req.Description;
                it.CoverPath = req.CoverPath;
                // Update items if provided
                if (req.Items != null && req.Items.Count > 0)
                {
                    it.Items = req.Items.Select(i => new ComicItemDto { Path = i.Path, Title = i.Title, ThumbPath = i.ThumbPath }).ToList();
                    it.Count = it.Items.Count;
                }
                Save();
                return it;
            }
        }
    }
}
