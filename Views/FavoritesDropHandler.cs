using System;
using System.Collections.Generic;
using System.Windows;
using GongSolutions.Wpf.DragDrop;

namespace ComicReader.Views
{
    /// <summary>
    /// Custom IDropTarget para FavoritesWindow.
    ///
    /// Resuelve el conflicto entre dos sistemas:
    ///   1) GongSolutions DragDrop (drag-to-reorder dentro de la lista) que
    ///      intercepta los eventos preview y marca e.Handled=true,
    ///   2) handlers nativos DragOver/Drop para aceptar archivos arrastrados
    ///      desde el Explorador de Windows.
    ///
    /// El branch de file-drop se aplica solo si el data tiene formato FileDrop;
    /// para drags internos (FavoriteComic) delegamos al DefaultDropHandler de
    /// la libreria, que ya implementa el reorder + multi-select correctamente.
    /// </summary>
    internal sealed class FavoritesDropHandler : DefaultDropHandler
    {
        private readonly Action<IEnumerable<string>> _onFilesDropped;
        private readonly Func<bool> _canAcceptFileDrop;
        private readonly Action _onInternalReorderCommitted;

        public FavoritesDropHandler(
            Action<IEnumerable<string>> onFilesDropped,
            Func<bool> canAcceptFileDrop,
            Action onInternalReorderCommitted)
        {
            _onFilesDropped = onFilesDropped ?? throw new ArgumentNullException(nameof(onFilesDropped));
            _canAcceptFileDrop = canAcceptFileDrop ?? throw new ArgumentNullException(nameof(canAcceptFileDrop));
            _onInternalReorderCommitted = onInternalReorderCommitted ?? throw new ArgumentNullException(nameof(onInternalReorderCommitted));
        }

        public override void DragOver(IDropInfo dropInfo)
        {
            if (IsFileDrop(dropInfo))
            {
                // Solo mostrar feedback de copia si hay una coleccion seleccionada
                // donde realmente se vayan a agregar los archivos. Sin esto el
                // cursor mostraba "+" pero el drop terminaba siendo no-op.
                if (!SafeCanAccept())
                {
                    dropInfo.Effects = DragDropEffects.None;
                    return;
                }
                dropInfo.Effects = DragDropEffects.Copy;
                dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
                return;
            }
            base.DragOver(dropInfo);
        }

        public override void Drop(IDropInfo dropInfo)
        {
            if (IsFileDrop(dropInfo))
            {
                if (!SafeCanAccept()) return;
                try
                {
                    var paths = ExtractFilePaths(dropInfo);
                    if (paths != null) _onFilesDropped(paths);
                }
                catch
                {
                    // No reventar el flujo de drop por errores en la logica
                    // del consumidor; el handler nativo ya manejaba excepciones.
                }
                return;
            }
            // Reorder interno: DefaultDropHandler mueve items dentro de
            // ItemsSource (FilteredItems), pero el modelo (Items de la
            // coleccion + CurrentCollectionItems) queda desincronizado. Se
            // notifica al consumidor para que reordene el modelo y persista.
            base.Drop(dropInfo);
            try { _onInternalReorderCommitted(); } catch { }
        }

        private bool SafeCanAccept()
        {
            try { return _canAcceptFileDrop(); } catch { return false; }
        }

        private static bool IsFileDrop(IDropInfo info)
        {
            try
            {
                if (info?.Data is IDataObject data)
                    return data.GetDataPresent(DataFormats.FileDrop);
            }
            catch { }
            return false;
        }

        private static IEnumerable<string> ExtractFilePaths(IDropInfo info)
        {
            try
            {
                if (info?.Data is IDataObject d && d.GetDataPresent(DataFormats.FileDrop))
                    return d.GetData(DataFormats.FileDrop) as string[];
            }
            catch { }
            return null;
        }
    }
}
