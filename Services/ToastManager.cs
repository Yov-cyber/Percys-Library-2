using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;

namespace ComicReader.Services
{
    public class ToastRequest
    {
        public string Message { get; set; }
        public string ActionLabel { get; set; }
        public Action Action { get; set; }
        public int DurationMs { get; set; }
        public ComicReader.Views.ToastWindow.ToastKind Kind { get; set; }
    }

    public sealed class ToastManager
    {
        private readonly ConcurrentQueue<ToastRequest> _queue = new ConcurrentQueue<ToastRequest>();
        private readonly SemaphoreSlim _sema = new SemaphoreSlim(1, 1);
        private static readonly Lazy<ToastManager> _inst = new Lazy<ToastManager>(() => new ToastManager());
        public static ToastManager Instance => _inst.Value;

        private ToastManager() { }

        public void Enqueue(string message, string actionLabel, Action action, int durationMs, ComicReader.Views.ToastWindow.ToastKind kind)
        {
            _queue.Enqueue(new ToastRequest { Message = message, ActionLabel = actionLabel, Action = action, DurationMs = durationMs, Kind = kind });
            _ = ProcessQueueAsync();
        }

        private async Task ProcessQueueAsync()
        {
            if (!await _sema.WaitAsync(0).ConfigureAwait(false)) return; // someone else is processing
            try
            {
                while (_queue.TryDequeue(out var req))
                {
                    try
                    {
                        // ensure shown on UI thread
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            try
                            {
                                await ComicReader.Views.ToastWindow.ShowToastAsync(req.Message, req.ActionLabel, req.Action, req.DurationMs, req.Kind).ConfigureAwait(false);
                            }
                            catch (Exception ex) { Logger.LogException("ToastManager.ProcessQueueAsync - ShowToastAsync failed", ex); }
                        }).Task.ConfigureAwait(false);
                    }
                    catch (Exception ex) { Logger.LogException("ToastManager.ProcessQueueAsync - invoking dispatcher failed", ex); }
                }
            }
            finally
            {
                _sema.Release();
            }
        }
    }
}
