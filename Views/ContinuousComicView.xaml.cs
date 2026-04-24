using System;
using System.Windows.Controls;
using ComicReader.ViewModels;
using ComicReader.Services;
using ComicReader.Core.Abstractions;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using System.Windows.Media;
using ComicReader.Utils;
using System.Windows.Media.Imaging;
using System.Threading;
using System.Threading.Tasks;

namespace ComicReader.Views
{
	public partial class ContinuousComicView : UserControl
	{
		public ContinuousComicViewModel ViewModel { get; }
        private ScrollViewer _innerScrollViewer;
	private double _lastVerticalOffset = 0;
	private DateTime _lastOffsetTime = DateTime.MinValue;

		public ContinuousComicView()
		{
			InitializeComponent();
			ViewModel = new ContinuousComicViewModel();
			DataContext = ViewModel;
			// Soportar zoom con Ctrl + rueda del ratón en la vista continua
			this.PreviewMouseWheel += ContinuousComicView_PreviewMouseWheel;
			this.Loaded += ContinuousComicView_Loaded;
			this.Unloaded += ContinuousComicView_Unloaded;

				// Escuchar cambios globales de ajustes para reaplicar filtros y tuning
				try
				{
					if (SettingsManager.Settings != null)
					{
						SettingsManager.Settings.PropertyChanged += (s, e) =>
						{
							try
							{
								var name = e.PropertyName;
								if (string.Equals(name, nameof(SettingsManager.Settings.Brightness), StringComparison.OrdinalIgnoreCase)
									|| string.Equals(name, nameof(SettingsManager.Settings.Contrast), StringComparison.OrdinalIgnoreCase))
								{
									// Reaplicar en visibles
									System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() => ReapplyBrightnessContrastVisible()));
								}
								else if (string.Equals(name, nameof(SettingsManager.Settings.PrefetchWindow), StringComparison.OrdinalIgnoreCase))
								{
									if (ViewModel?.Loader is ComicReader.Services.ComicPageLoader loader)
									{
										try { loader.SetPrefetchWindow(SettingsManager.Settings.PrefetchWindow); } catch { }
									}
								}
							}
							catch { }
						};
					}
				}
				catch { }
		}

		private void ContinuousComicView_Unloaded(object sender, RoutedEventArgs e)
		{
			try { ViewModel?.Dispose(); } catch { }
			try
			{
				Task[] arr;
				lock (_bgLock) { arr = _bgTasks.ToArray(); }
				if (arr != null && arr.Length > 0) Task.WaitAll(arr, 1200);
			}
			catch { }
		}

		private readonly System.Collections.Concurrent.ConcurrentDictionary<int, CancellationTokenSource> _deferredScrolls = new();

		private void StartDeferredScrollToIndex(ListBox list, ScrollViewer sv, int index)
		{
			// Cancelar cualquier solicitud previa para el mismo índice
			try { if (_deferredScrolls.TryGetValue(index, out var prev)) { try { prev.Cancel(); } catch { } } } catch { }
			var cts = new CancellationTokenSource();
			_deferredScrolls[index] = cts;
			var token = cts.Token;
			// Ejecutar en background para no bloquear UI; haremos pequeñas esperas y comprobar container
			var _deferredTask = Task.Run(async () =>
			{
				try
				{
					int attempts = 0;
					const int maxAttempts = 30; // unos ~600ms con delay 20ms
					while (!token.IsCancellationRequested && attempts++ < maxAttempts)
					{
						await System.Threading.Tasks.Task.Delay(20, token).ConfigureAwait(false);
						this.Dispatcher.Invoke(() =>
						{
							try
							{
								var cont = list.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem;
								if (cont != null)
								{
									// ya materializado: calcular posición y animar
									try
									{
										var itemTopInViewport = cont.TranslatePoint(new Point(0, 0), sv).Y;
										double tgt = Math.Max(0, Math.Min(Math.Max(0, sv.ScrollableHeight), sv.VerticalOffset + itemTopInViewport));
										if (Math.Abs(tgt - sv.VerticalOffset) > 0.5) AnimateScroll(sv, sv.VerticalOffset, tgt);
									}
									catch { }
									try { _deferredScrolls.TryRemove(index, out _); } catch { }
								}
							}
							catch { }
						});
					}
				}
				catch (OperationCanceledException) { }
				catch { }
				finally { try { _deferredScrolls.TryRemove(index, out _); } catch { } }
			}, token);
			// Track this task so it can be observed if needed
			TrackBackgroundTask(_deferredTask);
		}

			private readonly System.Collections.Generic.List<Task> _bgTasks = new System.Collections.Generic.List<Task>();
			private readonly object _bgLock = new object();
			private void TrackBackgroundTask(Task t)
			{
				if (t == null) return;
				lock (_bgLock) { _bgTasks.Add(t); }
				t.ContinueWith(_ => { try { lock (_bgLock) { _bgTasks.Remove(t); } } catch { } }, TaskScheduler.Default);
			}

        private void ContinuousComicView_Loaded(object sender, RoutedEventArgs e)
        {
			// Encontrar el ScrollViewer que ListBox usa internamente
			var listObj = this.FindName("PagesList") as DependencyObject;
			if (listObj != null)
			{
				_innerScrollViewer = FindDescendant<ScrollViewer>(listObj);
				if (_innerScrollViewer != null)
				{
					_innerScrollViewer.ScrollChanged += ContentScroll_ScrollChanged;
				}
			}
        }

		private void ContinuousComicView_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
		{
			try
			{
				if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
				{
					// Zoom alrededor del centro visible: incrementar/ reducir Zoom en ViewModel
					if (e.Delta > 0) ViewModel.Zoom *= 1.1;
					else ViewModel.Zoom *= 0.9;
					e.Handled = true;
				}
			}
			catch { }
		}

		// Ajustes para que la vista continua pueda hacer FitToScreen/Width/Height
		public void FitToWidth()
		{
			try
			{
					var sv = _innerScrollViewer ?? (this.FindName("ContentScroll") as ScrollViewer);
					if (sv == null) return;
				int idx = ViewModel.CurrentPage;
				var list = this.FindName("PagesList") as ListBox;
				var container = list?.ItemContainerGenerator.ContainerFromIndex(idx) as ListBoxItem;
				// fallback al viewport si no hay container
				double containerWidth = container?.ActualWidth ?? (sv.ViewportWidth - 32);
				var page = (ViewModel.Loader?.Pages != null && idx >= 0 && idx < ViewModel.Loader.Pages.Count) ? ViewModel.Loader.Pages[idx] : null;
				BitmapSource src = null;
				if (page != null) src = page.Image as BitmapSource;
				if (src == null)
				{
					// intentar con la imagen visible en el item
					var img = FindDescendant<Image>(container);
					src = img?.Source as BitmapSource;
				}
				if (src == null || src.PixelWidth <= 0) return;
				double newZoom = Math.Max(0.25, Math.Min(8.0, containerWidth / src.PixelWidth));
				ViewModel.Zoom = newZoom;
			}
			catch { }
		}

		public void FitToHeight()
		{
			try
			{
					var sv = _innerScrollViewer ?? (this.FindName("ContentScroll") as ScrollViewer);
					if (sv == null) return;
				int idx = ViewModel.CurrentPage;
				var list = this.FindName("PagesList") as ListBox;
				var container = list?.ItemContainerGenerator.ContainerFromIndex(idx) as ListBoxItem;
				double containerHeight = container?.ActualHeight ?? (sv.ViewportHeight - 32);
				var page = (ViewModel.Loader?.Pages != null && idx >= 0 && idx < ViewModel.Loader.Pages.Count) ? ViewModel.Loader.Pages[idx] : null;
				BitmapSource src = null;
				if (page != null) src = page.Image as BitmapSource;
				if (src == null)
				{
					var img = FindDescendant<Image>(container);
					src = img?.Source as BitmapSource;
				}
				if (src == null || src.PixelHeight <= 0) return;
				double newZoom = Math.Max(0.25, Math.Min(8.0, containerHeight / src.PixelHeight));
				ViewModel.Zoom = newZoom;
			}
			catch { }
		}

		public void FitToScreen()
		{
			try
			{
				var sv = _innerScrollViewer ?? (this.FindName("ContentScroll") as ScrollViewer);
				if (sv == null) return;
				int idx = ViewModel.CurrentPage;
				var list = this.FindName("PagesList") as ListBox;
				var container = list?.ItemContainerGenerator.ContainerFromIndex(idx) as ListBoxItem;
				double containerWidth = container?.ActualWidth ?? (sv.ViewportWidth - 32);
				double containerHeight = container?.ActualHeight ?? (sv.ViewportHeight - 32);
				var page = (ViewModel.Loader?.Pages != null && idx >= 0 && idx < ViewModel.Loader.Pages.Count) ? ViewModel.Loader.Pages[idx] : null;
				BitmapSource src = null;
				if (page != null) src = page.Image as BitmapSource;
				if (src == null)
				{
					var img = FindDescendant<Image>(container);
					src = img?.Source as BitmapSource;
				}
				if (src == null || src.PixelWidth <= 0 || src.PixelHeight <= 0) return;
				double scaleX = containerWidth / src.PixelWidth;
				double scaleY = containerHeight / src.PixelHeight;
				double newZoom = Math.Min(scaleX, scaleY);
				ViewModel.Zoom = Math.Max(0.25, Math.Min(8.0, newZoom));
			}
			catch { }
		}

		public IComicPageLoader ComicLoader
		{
			get => ViewModel.Loader;
			set => ViewModel.Loader = value;
		}

		private void ContentScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
		{
			// Llamamos a la sobrecarga que permita forzar materialización incluso si los items están virtualizados
			try
			{
				int approxCenter = -1;
				var sv = this.FindName("ContentScroll") as ScrollViewer ?? _innerScrollViewer;
				if (sv != null && ViewModel != null && ViewModel.Pages.Count > 0)
				{
					double viewportTop = sv.VerticalOffset;
					double viewportHeight = sv.ViewportHeight;
					// intentar estimar altura media de página
					double avgHeight = -1;
					var list = this.FindName("PagesList") as ListBox;
					if (list != null)
					{
						int counted = 0; double sum = 0;
						for (int i = 0; i < list.Items.Count; i++)
						{
							var container = list.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
							if (container != null)
							{
								sum += container.ActualHeight; counted++;
								if (counted >= 5) break;
							}
						}
						if (counted > 0) avgHeight = sum / counted;
					}
					if (avgHeight <= 0) avgHeight = Math.Max(200, viewportHeight / 3.0); // fallback
					// El centro del viewport
					double centerPos = viewportTop + viewportHeight / 2.0;
					approxCenter = (int)Math.Round(centerPos / avgHeight) - 1; // index zero-based
					approxCenter = Math.Max(0, Math.Min(ViewModel.Pages.Count - 1, approxCenter));
					ViewModel?.RequestVisiblePagesMaterialization(approxCenter);
				}
				else
				{
					ViewModel?.RequestVisiblePagesMaterialization();
				}
			}
			catch { ViewModel?.RequestVisiblePagesMaterialization(); }
			// Actualizar página actual aproximada basada en el elemento centrado/visible
			try
			{
				int nearest = FindNearestVisibleIndex();
				if (nearest >= 0 && nearest < ViewModel.Pages.Count && ViewModel.ShouldReactToUserScroll)
				{
					ViewModel.CurrentPage = nearest;
				}
				// Calcular velocidad de scroll (px / ms)
				if (_innerScrollViewer != null)
				{
					var now = DateTime.UtcNow;
					var offset = _innerScrollViewer.VerticalOffset;
					if (_lastOffsetTime != DateTime.MinValue)
					{
						var dt = (now - _lastOffsetTime).TotalMilliseconds;
						if (dt > 0)
						{
							var velocity = Math.Abs(offset - _lastVerticalOffset) / dt; // px per ms
							// Enviar heurística al loader si implementa métodos específicos
							if (ViewModel?.Loader is ComicReader.Services.ProgressivePageLoader p)
							{
								// Si velocidad alta, reducir prefetch agresividad
								if (velocity > 1.0) // heurístico
								{
									// cargar en baja calidad más lejanas (usamos PreloadPages que internamente respeta calidad)
									p.PreloadPages(ViewModel.CurrentPage);
								}
								else
								{
									p.PreloadPages(ViewModel.CurrentPage);
								}
								// Liberar distante según política
								p.ReleaseDistantPages(ViewModel.CurrentPage);
							}
						}
					}
					_lastOffsetTime = now;
					_lastVerticalOffset = _innerScrollViewer.VerticalOffset;
				}
			}
			catch { }
		}

		private void Image_Loaded(object sender, RoutedEventArgs e)
		{
			ViewModel?.RequestVisiblePagesMaterialization();
			try
			{
				var s = SettingsManager.Settings;
				if (s != null && sender is Image img)
				{
					var page = img.DataContext as ComicReader.Models.ComicPage;
					// Si la imagen principal ya está disponible, dejar el PageImage visible
					// sino garantizar que ThumbImage sea visible y PageImage esté en 0
					var container = FindAncestor<ListBoxItem>(img);
					if (container != null)
					{
						var thumb = FindDescendantByName<Image>(container, "ThumbImage");
						var full = FindDescendantByName<Image>(container, "PageImage");
						if (page?.Image != null)
						{
							if (full != null)
							{
								full.Opacity = 1;
							}
							if (thumb != null)
							{
								thumb.Opacity = 0;
							}
						}
						else
						{
							if (thumb != null) thumb.Opacity = 1;
							if (full != null) full.Opacity = 0;
						}
					}

					var baseSrc = page?.Image as BitmapSource ?? img.Source as BitmapSource;
					if (baseSrc != null && (Math.Abs(s.Brightness - 1.0) > 0.001 || Math.Abs(s.Contrast - 1.0) > 0.001))
					{
						img.Source = ImageAdjuster.ApplyBrightnessContrast(baseSrc, s.Brightness, s.Contrast);
					}
				}
			}
			catch { }
		}

		// Animar fade-in cuando la fuente de la imagen se actualice (low-res -> high-res)
		protected override void OnVisualParentChanged(DependencyObject oldParent)
		{
			base.OnVisualParentChanged(oldParent);
			// Registrar manejador para cambios de binding en las imágenes de items cuando existan
			var list = this.FindName("PagesList") as ListBox;
	
			if (list != null)
			{
				list.ItemContainerGenerator.StatusChanged += (s, ev) =>
				{
					// Intentamos suscribir a cada Image visible su TargetUpdated mediante el evento Loaded
					for (int i = 0; i < list.Items.Count; i++)
					{
						var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(i);
						if (container == null) continue;
						var img = FindDescendant<Image>(container);
						if (img != null)
						{
							img.TargetUpdated += Image_TargetUpdated;
						}
					}
				};
			}
		}

		private void Image_TargetUpdated(object sender, System.Windows.Data.DataTransferEventArgs e)
		{
			try
			{
				if (sender is Image img)
				{
					// si el Image que notificó es el PageImage (full), hacemos cross-fade entre ThumbImage -> PageImage
					var container = FindAncestor<ListBoxItem>(img);
					if (container != null)
					{
						var thumb = FindDescendantByName<Image>(container, "ThumbImage");
						var full = FindDescendantByName<Image>(container, "PageImage");
						if (full == img)
						{
							var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220))) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
							full.BeginAnimation(Image.OpacityProperty, fadeIn);
							if (thumb != null)
							{
								var fadeOut = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(220))) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
								thumb.BeginAnimation(Image.OpacityProperty, fadeOut);
							}
						}
						else
						{
							// si es la ThumbImage que cambió, dejarla visible hasta que la full llegue
							img.Opacity = 1;
						}
					}
				}
			}
			catch { }
		}

		private T FindDescendantByName<T>(DependencyObject parent, string name) where T : DependencyObject
		{
			if (parent == null) return null;
			int count = VisualTreeHelper.GetChildrenCount(parent);
			for (int i = 0; i < count; i++)
			{
				var child = VisualTreeHelper.GetChild(parent, i);
				if (child is FrameworkElement fe && fe.Name == name && child is T t) return t;
				var res = FindDescendantByName<T>(child, name);
				if (res != null) return res;
			}
			return null;
		}

		private T FindAncestor<T>(DependencyObject child) where T : DependencyObject
		{
			if (child == null) return null;
			var parent = VisualTreeHelper.GetParent(child);
			while (parent != null)
			{
				if (parent is T t) return t;
				parent = VisualTreeHelper.GetParent(parent);
			}
			return null;
		}

		// Panel flotante eliminado: temporizador y animaciones son no-op
		protected override void OnInitialized(EventArgs e)
		{
			base.OnInitialized(e);
		}

		public void ScrollToPage(int index)
		{
			if (index < 0 || index >= ViewModel.Pages.Count) return;
			try
			{
				ViewModel.BeginProgrammaticScroll();
				ViewModel.CurrentPage = index;
				// Traer al viewport el ítem solicitado
				var list = this.FindName("PagesList") as ListBox;
				list?.ScrollIntoView(ViewModel.Pages[index]);
			}
			finally
			{
				// liberar tras un breve diferido para evitar rebotes
				Dispatcher.BeginInvoke(new Action(() => ViewModel.EndProgrammaticScroll()), DispatcherPriority.Background);
			}
		}

		private int FindNearestVisibleIndex()
		{
			var list = this.FindName("PagesList") as ListBox;
			if (list == null || list.Items.Count == 0) return -1;
			// Calcular el elemento cuya posición está más cerca del centro del viewport
			var sv = this.FindName("ContentScroll") as ScrollViewer;
			if (sv == null) return -1;
			double viewportTop = sv.VerticalOffset;
			double viewportCenter = viewportTop + sv.ViewportHeight / 2.0;
			int bestIndex = -1;
			double bestDist = double.MaxValue;
			for (int i = 0; i < list.Items.Count; i++)
			{
				var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(i);
				if (container == null) continue; // aún virtualizado
				var transform = container.TransformToAncestor(list);
				var pos = transform.Transform(new Point(0, 0));
				double itemTop = pos.Y;
				double itemHeight = container.ActualHeight;
				double itemCenter = itemTop + itemHeight / 2.0;
				double dist = Math.Abs(itemCenter - viewportCenter);
				if (dist < bestDist)
				{
					bestDist = dist;
					bestIndex = i;
				}
			}
			return bestIndex;
		}

		public void ReapplyBrightnessContrastVisible()
		{
			try
			{
				var s = SettingsManager.Settings;
				if (s == null) return;
				var list = this.FindName("PagesList") as ListBox;
				if (list == null) return;
				int start = Math.Max(0, ViewModel.CurrentPage - 3);
				int end = Math.Min(ViewModel.Pages.Count - 1, ViewModel.CurrentPage + 3);
				for (int i = start; i <= end; i++)
				{
					var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(i);
					if (container == null) continue;
					var img = FindImageInContainer(container);
					if (img != null)
					{
						var page = container.DataContext as ComicReader.Models.ComicPage;
						var baseSrc = page?.Image as BitmapSource ?? img.Source as BitmapSource;
						if (baseSrc != null)
						{
							if (Math.Abs(s.Brightness - 1.0) < 0.001 && Math.Abs(s.Contrast - 1.0) < 0.001)
							{
								img.Source = baseSrc; // original
							}
							else
							{
								img.Source = ImageAdjuster.ApplyBrightnessContrast(baseSrc, s.Brightness, s.Contrast);
							}
						}
					}
				}
			}
			catch { }
		}

		private Image FindImageInContainer(ListBoxItem container)
		{
			try
			{
				return FindDescendant<Image>(container);
			}
			catch { return null; }
		}

		private T FindDescendant<T>(DependencyObject parent) where T : DependencyObject
		{
			if (parent == null) return null;
			int count = VisualTreeHelper.GetChildrenCount(parent);
			for (int i = 0; i < count; i++)
			{
				var child = VisualTreeHelper.GetChild(parent, i);
				if (child is T t) return t;
				var res = FindDescendant<T>(child);
				if (res != null) return res;
			}
			return null;
		}
	}
}

// Extensiones parciales: métodos públicos para controlar scroll desde el MainWindow
namespace ComicReader.Views
{
	public partial class ContinuousComicView : UserControl
	{
		/// <summary>
		/// Desplaza la vista una página hacia arriba/abajo (snap) y devuelve true si hubo desplazamiento.
		/// </summary>
		public bool ScrollOnePage(bool down)
		{
			try
			{
				var sv = _innerScrollViewer ?? (this.FindName("ContentScroll") as ScrollViewer);
				if (sv == null)
				{
					ComicReader.Utils.DevLogger.Debug("ScrollOnePage: no ScrollViewer found (_innerScrollViewer and ContentScroll both null)");
					return false;
				}
				double cur = sv.VerticalOffset;
				double max = Math.Max(0, sv.ScrollableHeight);
				ComicReader.Utils.DevLogger.Debug($"ScrollOnePage: down={down}, cur={cur}, max={max}, viewport={sv.ViewportHeight}");
				if (max < 0.5) { ComicReader.Utils.DevLogger.Debug("ScrollOnePage: maxOffset < 0.5 -> no scroll"); return false; }

				int current = ViewModel?.CurrentPage ?? 0;
				int desired = down ? Math.Min((ViewModel?.PagesCount ?? 0) - 1, current + 1) : Math.Max(0, current - 1);
				var list = this.FindName("PagesList") as ListBox;
				if (list != null)
				{
					var container = list.ItemContainerGenerator.ContainerFromIndex(desired) as ListBoxItem;
					if (container == null)
					{
						ComicReader.Utils.DevLogger.Debug($"ScrollOnePage: container for index {desired} is null (virtualized). Calling ScrollIntoView and scheduling deferred scroll task.");
						var item = ViewModel?.Pages != null && desired >= 0 && desired < ViewModel.Pages.Count ? ViewModel.Pages[desired] : null;
						if (item != null)
						{
							try { list.ScrollIntoView(item); } catch { }
							// Lanzar tarea que esperará a que el contenedor sea materializado (o hasta timeout) antes de animar.
							StartDeferredScrollToIndex(list, sv, desired);
							return true;
						}
						ComicReader.Utils.DevLogger.Debug($"ScrollOnePage: no item to ScrollIntoView for index {desired}");
					}
					else
					{
						// Calcular la posición del item relativa al ScrollViewer
						try
						{
							var itemTopInViewport = container.TranslatePoint(new Point(0, 0), sv).Y;
							double target = Math.Max(0, Math.Min(max, sv.VerticalOffset + itemTopInViewport));
							ComicReader.Utils.DevLogger.Debug($"ScrollOnePage: found container index={desired}, itemTopInViewport={itemTopInViewport}, target={target}");
							if (Math.Abs(target - cur) < 0.5) { ComicReader.Utils.DevLogger.Debug("ScrollOnePage: target too close to current; aborting"); return false; }
							// Animar desplazamiento
							AnimateScroll(sv, cur, target);
							return true;
						}
						catch { ComicReader.Utils.DevLogger.Debug("ScrollOnePage: error computing translatepoint"); return false; }
					}
				}

				// Fallback: desplazar viewport completo
				double ratio = SettingsManager.Settings?.PageScrollStepRatio > 0 ? SettingsManager.Settings.PageScrollStepRatio : 0.9;
				double step = Math.Max(24, sv.ViewportHeight * ratio);
				double fallbackTarget = down ? Math.Min(max, cur + step) : Math.Max(0, cur - step);
				if (Math.Abs(fallbackTarget - cur) < 0.5) return false;
				AnimateScroll(sv, cur, fallbackTarget);
				return true;
			}
			catch { return false; }
		}

		private void AnimateScroll(ScrollViewer sv, double from, double to)
		{
			try
			{
				var anim = new System.Windows.Media.Animation.DoubleAnimation
				{
					From = from,
					To = to,
					Duration = new Duration(TimeSpan.FromMilliseconds(240)),
					EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
				};

				var proxy = new DependencyObject();
				var dp = DependencyProperty.RegisterAttached("ScrollAnimLocal", typeof(double), typeof(ContinuousComicView), new PropertyMetadata(0.0, (d, e) =>
				{
					try { sv.ScrollToVerticalOffset((double)e.NewValue); } catch { }
				}));

				var sb = new System.Windows.Media.Animation.Storyboard();
				sb.Children.Add(anim);
				System.Windows.Media.Animation.Storyboard.SetTarget(anim, proxy);
				System.Windows.Media.Animation.Storyboard.SetTargetProperty(anim, new PropertyPath(dp));
				sb.Begin();
			}
			catch { }
		}
	}
}
