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

        public FavoritesDropHandler(Action<IEnumerable<string>> onFilesDropped)
        {
            _onFilesDropped = onFilesDropped ?? throw new ArgumentNullException(nameof(onFilesDropped));
        }

        public override void DragOver(IDropInfo dropInfo)
        {
            if (IsFileDrop(dropInfo))
            {
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
            base.Drop(dropInfo);
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
