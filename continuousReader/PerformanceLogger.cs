using System;
using System.Diagnostics;
using ComicReader.Services;

namespace ComicReader.ContinuousReader
{
        public static class PerformanceLogger
        {
                // Por defecto la instrumentacin de performance est desactivada en Release para no afectar runtime.
                // Se puede forzar activacin en Debug builds o mediante la propiedad Enabled en runtime.
#if DEBUG
                private static readonly bool _defaultEnabled = true;
#else
                private static readonly bool _defaultEnabled = false;
#endif

                /// <summary>
                /// Si true, los logs/mediciones se registran. Por defecto false en Release.
                /// Se puede activar manualmente (por ejemplo desde settings) para debugging.
                /// </summary>
                public static bool Enabled { get; set; } = _defaultEnabled;

                public static Action<string> LogAction { get; set; } = null;

                        public static void Log(string message)
                        {
                                try
                                {
                                        // Allow enabling from settings at runtime: SettingsManager.Settings.EnablePerfLogs
                                        bool runtimeEnabled = Enabled || Debugger.IsAttached;
                                        try { runtimeEnabled = runtimeEnabled || (SettingsManager.Settings?.EnablePerfLogs == true); } catch { }
                                        if (!runtimeEnabled) return;
                                        var text = $"[Perf] {DateTime.UtcNow:O} - {message}";
                                        if (LogAction != null) LogAction.Invoke(text);
                                        try { ComicReader.Services.Logger.Log(text, ComicReader.Core.Abstractions.LogLevel.Info); } catch { }
                                        System.Diagnostics.Debug.WriteLine(text);
                                }
                                catch { }
                        }

                        public static IDisposable Measure(string name)
                        {
                                try
                                {
                                        bool runtimeEnabled = Enabled || Debugger.IsAttached;
                                        try { runtimeEnabled = runtimeEnabled || (SettingsManager.Settings?.EnablePerfLogs == true); } catch { }
                                        if (!runtimeEnabled) return NoOpDisposable.Instance;
                                        var sw = System.Diagnostics.Stopwatch.StartNew();
                                        return new DisposableAction(() => Log($"{name} took {sw.ElapsedMilliseconds}ms"));
                                }
                                catch { return NoOpDisposable.Instance; }
                        }

                private class DisposableAction : IDisposable
                {
                        private readonly Action _onDispose;
                        private bool _disposed;
                        public DisposableAction(Action onDispose) => _onDispose = onDispose;
                        public void Dispose()
                        {
                                if (_disposed) return;
                                _disposed = true;
                                try { _onDispose(); } catch { }
                        }
                }

                private class NoOpDisposable : IDisposable
                {
                        public static readonly NoOpDisposable Instance = new NoOpDisposable();
                        public void Dispose() { }
                }
        }
        }
