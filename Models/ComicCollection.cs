using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ComicReader.Models
{
    /// <summary>
    /// Modelo avanzado para representar una colección de cómics.
    /// Incluye helpers para gestión de items, cover derivado y metadatos adicionales.
    /// </summary>
    public class ComicCollection : INotifyPropertyChanged
    {
        private Guid _id;
        private string _name;
        private string _description;
        private DateTime _dateCreated;
        private string _color;
        private ObservableCollection<FavoriteComic> _items = new ObservableCollection<FavoriteComic>();

        public Guid Id { get => _id; set { _id = value; Raise(); } }

        public string Name
        {
            get => _name;
            set { _name = value; Raise(); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; Raise(); }
        }

        public DateTime DateCreated
        {
            get => _dateCreated;
            set { _dateCreated = value; Raise(); }
        }

        /// <summary>Color o etiqueta de color para mostrar en la UI (hex o nombre)</summary>
        public string Color
        {
            get => _color;
            set { _color = value; Raise(); }
        }

        /// <summary>Lista observable de favoritos</summary>
        public ObservableCollection<FavoriteComic> Items
        {
            get => _items;
            set
            {
                if (_items == value) return;
                if (_items != null) _items.CollectionChanged -= Items_CollectionChanged;
                _items = value ?? new ObservableCollection<FavoriteComic>();
                _items.CollectionChanged += Items_CollectionChanged;
                Raise();
                Raise(nameof(ItemCount));
            }
        }

        private void Items_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Raise(nameof(ItemCount));
            Raise(nameof(CoverImage));
        }

        /// <summary>Número de items en la colección</summary>
        public int ItemCount => Items?.Count ?? 0;

        /// <summary>Ruta de imagen representativa de la colección (thumbnail del primer cómic si existe)</summary>
        public string CoverImage => Items?.FirstOrDefault()?.Thumbnail ?? null;

        public ComicCollection()
        {
            Id = Guid.NewGuid();
            DateCreated = DateTime.UtcNow;
            _items.CollectionChanged += Items_CollectionChanged;
        }

        // Helpers para manejo de items
        public void Add(FavoriteComic fav)
        {
            if (fav == null) return;
            Items.Add(fav);
            Raise(nameof(ItemCount));
        }

        public bool Remove(FavoriteComic fav)
        {
            var removed = Items.Remove(fav);
            if (removed) Raise(nameof(ItemCount));
            return removed;
        }

        public void Move(int oldIndex, int newIndex)
        {
            if (oldIndex == newIndex) return;
            if (oldIndex < 0 || oldIndex >= Items.Count) return;
            if (newIndex < 0 || newIndex >= Items.Count) return;
            var item = Items[oldIndex];
            Items.RemoveAt(oldIndex);
            Items.Insert(newIndex, item);
            Raise(nameof(Items));
        }

        // Simple serialization helper hooks (caller can use System.Text.Json)
        public override string ToString() => $"Collection: {Name} ({ItemCount} items)";

        public event PropertyChangedEventHandler PropertyChanged;

        protected void Raise([CallerMemberName] string propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}
