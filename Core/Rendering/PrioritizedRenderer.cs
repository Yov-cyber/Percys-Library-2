using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ComicReader.Core.Rendering
{
	// Implementación mínima de un renderer priorizado que cola acciones y las ejecuta de forma asíncrona.
	// Se puede extender para prioridades reales y cancelación.
	public class PrioritizedRenderer : IDisposable
	{
		// Two internal queues: high priority and low priority.
		private readonly BlockingCollection<Func<Task>> _highQueue = new();
		private readonly BlockingCollection<Func<Task>> _lowQueue = new();
	private readonly Task _worker;
	private readonly CancellationTokenSource _workerCts = new CancellationTokenSource();

		// Instrumentation
		private long _highProcessed;
		private long _lowProcessed;
		private long _totalExecutionTicks;
		private readonly Action<string> _logger;
		private bool _disposed;

		/// <summary>
		/// Crea un PrioritizedRenderer.
		/// Si se pasa un logger, la clase publicará mensajes de instrumentación informativos.
		/// </summary>
		public PrioritizedRenderer(Action<string> logger = null)
		{
			_logger = logger;
			_worker = Task.Run(async () =>
			{
				var ct = _workerCts.Token;
				try
				{
					while ((!_highQueue.IsCompleted || !_lowQueue.IsCompleted) && !ct.IsCancellationRequested)
					{
						Func<Task> work = null;
						try
						{
							// Preferir trabajos de alta prioridad
							if (_highQueue.TryTake(out work, 250))
							{
								var sw = System.Diagnostics.Stopwatch.StartNew();
								if (work != null)
									await ExecuteSafe(work).ConfigureAwait(false);
								sw.Stop();
								System.Threading.Interlocked.Increment(ref _highProcessed);
								System.Threading.Interlocked.Add(ref _totalExecutionTicks, sw.ElapsedTicks);
								continue;
							}

							// Si no hay alta prioridad, procesar baja prioridad
							if (_lowQueue.TryTake(out work, 250))
							{
								var swLow = System.Diagnostics.Stopwatch.StartNew();
								if (work != null)
									await ExecuteSafe(work).ConfigureAwait(false);
								swLow.Stop();
								System.Threading.Interlocked.Increment(ref _lowProcessed);
								System.Threading.Interlocked.Add(ref _totalExecutionTicks, swLow.ElapsedTicks);
								continue;
							}

							// Si ninguna cola tiene trabajo, loop y esperar
						}
						catch { /* ignorar errores por trabajo individual */ }
					}
				}
				catch { }
			}, _workerCts.Token);
		}

		private static async Task ExecuteSafe(Func<Task> work)
		{
			if (work == null) return;
			try { await work().ConfigureAwait(false); } catch { }
		}

		public void Enqueue(Func<Task> work, bool highPriority = false)
		{
			if (_disposed) throw new ObjectDisposedException(nameof(PrioritizedRenderer));
			if (highPriority) _highQueue.Add(work);
			else _lowQueue.Add(work);
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			try { _highQueue.CompleteAdding(); } catch { }
			try { _lowQueue.CompleteAdding(); } catch { }
			try { _workerCts.Cancel(); } catch { }
			try { _worker.Wait(1000); } catch { }
			try { _highQueue.Dispose(); } catch { }
			try { _lowQueue.Dispose(); } catch { }
			try { _workerCts.Dispose(); } catch { }
		}

		/// <summary>
		/// Obtiene estadísticas acumuladas desde la creación o desde el último reset.
		/// </summary>
		public RendererStats GetStatsAndReset()
		{
			var stats = new RendererStats
			{
				HighProcessed = System.Threading.Interlocked.Exchange(ref _highProcessed, 0),
				LowProcessed = System.Threading.Interlocked.Exchange(ref _lowProcessed, 0),
				TotalExecutionMilliseconds = TimeSpan.FromTicks(System.Threading.Interlocked.Exchange(ref _totalExecutionTicks, 0)).TotalMilliseconds
			};
			if (_logger != null)
			{
				_logger($"PrioritizedRenderer stats: H={stats.HighProcessed} L={stats.LowProcessed} ms={stats.TotalExecutionMilliseconds:F1}");
			}
			return stats;
		}

		public record RendererStats
		{
			public long HighProcessed { get; init; }
			public long LowProcessed { get; init; }
			public double TotalExecutionMilliseconds { get; init; }
		}
	}
}

