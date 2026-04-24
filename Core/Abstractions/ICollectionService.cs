using System;
using System.Collections.Generic;

namespace ComicReader.Core.Abstractions
{
    public interface ICollectionService
    {
        IEnumerable<CollectionDto> GetAll();
        CollectionDto Create(CollectionCreateRequest req);
        CollectionDto Update(Guid id, CollectionCreateRequest req);
        CollectionDto Rename(Guid id, string newName);
        CollectionDto Duplicate(Guid id);
        void Delete(Guid id);
    // Re-insert a collection preserving its DTO (useful for undo)
    // Optional index specifies the position to insert in the list; if null, append to the end.
    void AddRaw(CollectionDto dto, int? index = null);
        void ExportCollections(string path);
        void ImportCollections(string path);
    }

    public class CollectionDto
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; }
        public string Description { get; set; }
        public string CoverPath { get; set; }
        public int Count { get; set; }
        public List<ComicItemDto> Items { get; set; } = new List<ComicItemDto>();
    }

    public class ComicItemDto : System.ComponentModel.INotifyPropertyChanged
    {
        private string _title;
        private string _path;
        private string _thumbPath;
        private bool _isFavorite;

        public string Title { get => _title; set { if (_title != value) { _title = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Title))); } } }
        public string Path { get => _path; set { if (_path != value) { _path = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Path))); } } }
        public string ThumbPath { get => _thumbPath; set { if (_thumbPath != value) { _thumbPath = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ThumbPath))); } } }
        // Mark this comic as favorite (UI toggle). Persisted with collections.
        public bool IsFavorite { get => _isFavorite; set { if (_isFavorite != value) { _isFavorite = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsFavorite))); } } }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    public class CollectionCreateRequest
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string CoverPath { get; set; }
        public List<ComicItemDto> Items { get; set; } = new List<ComicItemDto>();
    }
}
