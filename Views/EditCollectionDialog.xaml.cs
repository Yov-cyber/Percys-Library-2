using System;
using System.Windows;
using ComicReader.Core.Abstractions;
using Microsoft.Win32;
using ComicReader.Core.Services;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Input;

namespace ComicReader.Views
{
    public partial class EditCollectionDialog : Window
    {
        public string CollectionName
        {
            get
            {
                try { var tb = this.FindName("NameBox") as System.Windows.Controls.TextBox; return tb?.Text?.Trim() ?? string.Empty; } catch { return string.Empty; }
            }
            set
            {
                try { var tb = this.FindName("NameBox") as System.Windows.Controls.TextBox; if (tb != null) tb.Text = value; } catch { }
            }
        }

        public string Description
        {
            get
            {
                try { var tb = this.FindName("DescBox") as System.Windows.Controls.TextBox; return tb?.Text?.Trim() ?? string.Empty; } catch { return string.Empty; }
            }
            set
            {
                try { var tb = this.FindName("DescBox") as System.Windows.Controls.TextBox; if (tb != null) tb.Text = value; } catch { }
            }
        }
        public string CoverPath { get; set; }

    private System.Windows.Point _dragStartPoint;
    private readonly Stack<(List<Core.Abstractions.ComicItemDto> items, int index)> _undoStack = new Stack<(List<Core.Abstractions.ComicItemDto>, int)>();
    private readonly System.Collections.ObjectModel.ObservableCollection<ViewModels.ComicItemViewModel> _itemsCollection = new System.Collections.ObjectModel.ObservableCollection<ViewModels.ComicItemViewModel>();

        public EditCollectionDialog()
        {
            try
            {
                var mi = this.GetType().GetMethod("InitializeComponent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                mi?.Invoke(this, null);
            }
            catch { }
            // Bind the List control to the observable collection so UI updates when properties change
            try { var itemsList = this.FindName("ItemsList") as System.Windows.Controls.ListBox; if (itemsList != null) itemsList.ItemsSource = _itemsCollection; } catch { }
        }

        // Helper to set the cover path and update the UI text safely from callers
        public void SetCoverPath(string path)
        {
            CoverPath = path;
            try
            {
                var tb = this.FindName("CoverPathText") as System.Windows.Controls.TextBox;
                if (tb != null) tb.Text = path ?? string.Empty;
            }
            catch { }
        }

        // Public API remains List<ComicItemDto> for compatibility with callers.
        public List<Core.Abstractions.ComicItemDto> Items
        {
            get => _itemsCollection.Select(vm => vm.ToDto()).ToList();
            set
            {
                _itemsCollection.Clear();
                if (value == null) return;
                foreach (var it in value) _itemsCollection.Add(new ViewModels.ComicItemViewModel(it));
            }
        }

        private void AddComics_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = "Cómics|*.cbz;*.cbr;*.zip;*.rar|Todos los archivos|*.*";
            dlg.Multiselect = true;
            if (dlg.ShowDialog(this) == true)
            {
                foreach (var f in dlg.FileNames)
                {
                    var dto = new Core.Abstractions.ComicItemDto { Path = f, Title = System.IO.Path.GetFileNameWithoutExtension(f), ThumbPath = string.Empty };
                    _itemsCollection.Add(new ViewModels.ComicItemViewModel(dto));
                }
            }
        }

        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var itemsList = this.FindName("ItemsList") as System.Windows.Controls.ListBox;
            var selectedVMs = itemsList?.SelectedItems.Cast<ViewModels.ComicItemViewModel>().ToList() ?? new System.Collections.Generic.List<ViewModels.ComicItemViewModel>();
            if (selectedVMs.Count == 0) return;
            int firstIndex = _itemsCollection.IndexOf(selectedVMs.First());
            var selectedDtos = selectedVMs.Select(vm => vm.ToDto()).ToList();
            _undoStack.Push((selectedDtos, firstIndex));
            foreach (var s in selectedVMs) _itemsCollection.Remove(s);
            try { var ub = this.FindName("UndoButton") as System.Windows.Controls.Button; if (ub != null) ub.IsEnabled = true; } catch { }

            // Also register with central undo service so the user sees a global toast undo
            try
            {
                var undoSvc = ServiceLocator.TryGet<ComicReader.Services.IUndoService>();
                if (undoSvc != null)
                {
                    // Capture data for the action
                    var dtos = selectedDtos.Select(d => new Core.Abstractions.ComicItemDto { Path = d.Path, Title = d.Title, ThumbPath = d.ThumbPath }).ToList();
                    var idx = firstIndex;
                    undoSvc.Register($"{dtos.Count} cómics eliminados", "Deshacer", () =>
                    {
                        try
                        {
                            // Avoid duplicate insertion if local undo already restored
                            bool alreadyPresent = dtos.All(d => _itemsCollection.Any(vm => string.Equals(vm.Path, d.Path, StringComparison.OrdinalIgnoreCase)));
                            if (alreadyPresent) return;
                            var insertAt = Math.Min(idx, _itemsCollection.Count);
                            foreach (var it in dtos)
                            {
                                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                                {
                                    try { _itemsCollection.Insert(insertAt++, new ViewModels.ComicItemViewModel(it)); } catch { }
                                });
                            }
                        }
                        catch { }
                    });
                }
            }
            catch { }
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_undoStack.Count == 0) return;
            var (items, index) = _undoStack.Pop();
            int insertAt = Math.Min(index, _itemsCollection.Count);
            foreach (var it in items)
            {
                _itemsCollection.Insert(insertAt++, new ViewModels.ComicItemViewModel(it));
            }
            try { var ub = this.FindName("UndoButton") as System.Windows.Controls.Button; if (ub != null) ub.IsEnabled = _undoStack.Count > 0; } catch { }
        }

        private void SelectCover_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp|Todos los archivos|*.*";
            if (dlg.ShowDialog(this) == true)
            {
                CoverPath = dlg.FileName;
                try { SetCoverPath(CoverPath); } catch { }
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CollectionName))
            {
                MessageBox.Show(this, "El nombre no puede estar vacío.", "Nombre requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
            Close();
        }

        // Drag & drop handlers for ItemsList
        private void ItemsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void ItemsList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            var list = this.FindName("ItemsList") as System.Windows.Controls.ListBox;
            var itemVm = list?.SelectedItem as ViewModels.ComicItemViewModel;
            if (itemVm == null) return;

            var data = new DataObject("ComicItem", itemVm);
            DragDrop.DoDragDrop(list, data, DragDropEffects.Move | DragDropEffects.Copy);
        }

        private void ItemsList_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent("ComicItem"))
                e.Effects = DragDropEffects.Copy | DragDropEffects.Move;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void ItemsList_Drop(object sender, DragEventArgs e)
        {
            var list = this.FindName("ItemsList") as System.Windows.Controls.ListBox;
            if (list == null) return;
            var point = e.GetPosition(list);
            int index = GetCurrentIndex(point);

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var f in files)
                {
                    var dto = new Core.Abstractions.ComicItemDto { Path = f, Title = System.IO.Path.GetFileNameWithoutExtension(f), ThumbPath = string.Empty };
                    if (index >= 0 && index <= _itemsCollection.Count)
                    {
                        _itemsCollection.Insert(index++, new ViewModels.ComicItemViewModel(dto));
                    }
                    else
                    {
                        _itemsCollection.Add(new ViewModels.ComicItemViewModel(dto));
                    }
                }
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent("ComicItem"))
            {
                var draggedVm = e.Data.GetData("ComicItem") as ViewModels.ComicItemViewModel;
                if (draggedVm == null) return;
                int oldIndex = _itemsCollection.IndexOf(draggedVm);
                if (oldIndex >= 0) _itemsCollection.RemoveAt(oldIndex);
                if (index > _itemsCollection.Count) index = _itemsCollection.Count;
                _itemsCollection.Insert(index, draggedVm);
                e.Handled = true;
            }
        }

        private int GetCurrentIndex(System.Windows.Point point)
        {
            var list = this.FindName("ItemsList") as System.Windows.Controls.ListBox;
            if (list == null)
            {
                return _itemsCollection.Count;
            }
            for (int i = 0; i < _itemsCollection.Count; i++)
            {
                var item = list.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (item == null) continue;
                var bounds = new Rect(item.TranslatePoint(new System.Windows.Point(0, 0), list), item.RenderSize);
                if (bounds.Contains(point)) return i;
            }
            return _itemsCollection.Count;
        }
    }
}
