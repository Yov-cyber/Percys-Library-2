using System;
using System.Collections.Generic;

namespace ComicReader.Services
{
    public class UndoService : IUndoService
    {
        private readonly Action<string, string, Action> _toastInvoker;
        private readonly Stack<Action> _pending = new Stack<Action>();

        public UndoService(Action<string, string, Action> toastInvoker)
        {
            _toastInvoker = toastInvoker ?? ((m, l, a) => { });
        }

        public void Register(string message, string label, Action undoAction)
        {
            if (undoAction == null) return;
            _pending.Push(undoAction);
            try
            {
                _toastInvoker?.Invoke(message, label, () =>
                {
                    try { undoAction(); } catch { }
                });
            }
            catch { }
        }

        public void InvokeLast()
        {
            if (_pending.Count == 0) return;
            var a = _pending.Pop();
            try { a(); } catch { }
        }
    }
}
