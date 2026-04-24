using System;

namespace ComicReader.Services
{
    public interface IUndoService
    {
        /// <summary>
        /// Register an undoable action with a message and label for the UI. The provided undoAction
        /// will be invoked when the user triggers the undo (or can be invoked programmatically for tests).
        /// </summary>
        void Register(string message, string label, Action undoAction);

        /// <summary>
        /// For tests: invoke the most recently registered undo action (if any).
        /// </summary>
        void InvokeLast();
    }
}
