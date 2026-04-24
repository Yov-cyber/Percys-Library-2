using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using ComicReader.Commands;
using ComicReader.Models;

#nullable enable

namespace ComicReader.ViewModels
{
    public class CollectionViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<ComicCollection> _allCollections = new ObservableCollection<ComicCollection>();
        
        public ObservableCollection<ComicCollection> Collections { get; } = new ObservableCollection<ComicCollection>();

        private ComicCollection? _selected;
        public ComicCollection? SelectedCollection
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                OnPropertyChanged(nameof(SelectedCollection));
                OnPropertyChanged(nameof(HasSelectedCollection));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool HasSelectedCollection => SelectedCollection != null;

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                OnPropertyChanged(nameof(SearchText));
                ApplyFilter();
            }
        }

        private string _sortBy = "Name";
        public string SortBy
        {
            get => _sortBy;
            set
            {
                if (_sortBy == value) return;
                _sortBy = value;
                OnPropertyChanged(nameof(SortBy));
                ApplyFilter();
            }
        }

        public ICommand AddCollectionCommand { get; }
        public ICommand RemoveCollectionCommand { get; }
        public ICommand AddFavoriteCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand RefreshCommand { get; }

        public CollectionViewModel()
        {
            AddCollectionCommand = new RelayCommand(_ => AddCollection());
            RemoveCollectionCommand = new RelayCommand(_ => RemoveSelectedCollection(), _ => SelectedCollection != null);
            AddFavoriteCommand = new RelayCommand(_ => AddSampleFavorite(), _ => SelectedCollection != null);
            ClearSearchCommand = new RelayCommand(_ => SearchText = string.Empty);
            RefreshCommand = new RelayCommand(_ => ApplyFilter());
        }

        private void AddCollection()
        {
            var c = new ComicCollection { Name = "Nueva colección" };
            Collections.Add(c);
            SelectedCollection = c;
        }

        private void RemoveSelectedCollection()
        {
            if (SelectedCollection != null)
                Collections.Remove(SelectedCollection);
        }

        private void AddSampleFavorite()
        {
            if (SelectedCollection == null) return;
            var fav = new FavoriteComic { Title = "Nuevo Favorito", Author = "Desconocido", Thumbnail = null, TotalPages = 100 };
            SelectedCollection.Add(fav);
        }

        /// <summary>
        /// Aplica filtro de búsqueda y ordenamiento en tiempo real
        /// </summary>
        private void ApplyFilter()
        {
            try
            {
                IEnumerable<ComicCollection> filtered = _allCollections;

                // Filtrar por texto de búsqueda
                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    var search = SearchText.ToLowerInvariant();
                    filtered = filtered.Where(c => 
                        c.Name.ToLowerInvariant().Contains(search) ||
                        c.Items.Any(item => 
                            item.Title.ToLowerInvariant().Contains(search) ||
                            (item.Author?.ToLowerInvariant().Contains(search) ?? false)
                        )
                    );
                }

                // Ordenar según criterio
                filtered = SortBy switch
                {
                    "Name" => filtered.OrderBy(c => c.Name),
                    "ItemCount" => filtered.OrderByDescending(c => c.Items.Count),
                    _ => filtered.OrderBy(c => c.Name)
                };

                // Actualizar colección observable
                Collections.Clear();
                foreach (var collection in filtered)
                {
                    Collections.Add(collection);
                }

                ComicReader.Utils.ModernLogger.Debug($"Filtrado aplicado: {Collections.Count}/{_allCollections.Count} colecciones mostradas");
            }
            catch (Exception ex)
            {
                ComicReader.Utils.ModernLogger.Error(ex, "Error al aplicar filtro en colecciones");
            }
        }

        /// <summary>
        /// Agrega una colección a la lista completa y aplica filtro
        /// </summary>
        public void AddCollectionToList(ComicCollection collection)
        {
            _allCollections.Add(collection);
            ApplyFilter();
        }

        /// <summary>
        /// Remueve una colección de la lista completa
        /// </summary>
        public void RemoveCollectionFromList(ComicCollection collection)
        {
            _allCollections.Remove(collection);
            ApplyFilter();
        }

        /// <summary>
        /// Carga todas las colecciones iniciales
        /// </summary>
        public void LoadCollections(IEnumerable<ComicCollection> collections)
        {
            _allCollections.Clear();
            foreach (var collection in collections)
            {
                _allCollections.Add(collection);
            }
            ApplyFilter();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
