// FileName: Services/Loading/LoadingService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ComicReader.Services.Notifications;

namespace ComicReader.Services.Loading
{
    /// <summary>
    /// Sistema de carga premium con progress bars, skeleton loaders y feedback visual
    /// </summary>
    public class LoadingService
    {
        private static LoadingService _instance;
        public static LoadingService Instance => _instance ??= new LoadingService();

        private Window _mainWindow;

        private LoadingService() { }

        public void Initialize(Window mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        }

        #region Public API

        /// <summary>
        /// Ejecuta una operación con indicador de progreso
        /// </summary>
        public async Task<T> ExecuteWithProgressAsync<T>(
            Func<IProgress<LoadingProgress>, CancellationToken, Task<T>> operation,
            string title = "Procesando...",
            bool cancellable = false)
        {
            var toast = NotificationService.Instance.Progress(title, title);
            var progress = new Progress<LoadingProgress>(p =>
            {
                var message = string.IsNullOrEmpty(p.Message) ? title : p.Message;
                NotificationService.Instance.UpdateProgress(toast, p.Percentage, message);
            });

            var cts = cancellable ? new CancellationTokenSource() : null;

            try
            {
                var result = await operation(progress, cts?.Token ?? CancellationToken.None);
                NotificationService.Instance.Close(toast);
                return result;
            }
            catch (OperationCanceledException)
            {
                NotificationService.Instance.Close(toast);
                NotificationService.Instance.Warning("Operación cancelada", "Cancelado");
                throw;
            }
            catch
            {
                NotificationService.Instance.Close(toast);
                throw;
            }
            finally
            {
                cts?.Dispose();
            }
        }

        /// <summary>
        /// Muestra un overlay de carga en toda la ventana
        /// </summary>
        public LoadingOverlay ShowOverlay(string message = "Cargando...", bool showProgress = false)
        {
            LoadingOverlay overlay = null;

            _mainWindow?.Dispatcher.Invoke(() =>
            {
                if (_mainWindow.Content is Grid mainGrid)
                {
                    overlay = new LoadingOverlay(message, showProgress);
                    mainGrid.Children.Add(overlay);
                    Panel.SetZIndex(overlay, 10000);
                }
            });

            return overlay;
        }

        /// <summary>
        /// Crea un skeleton loader para una lista
        /// </summary>
        public SkeletonLoader CreateSkeletonLoader(int itemCount = 5, double itemHeight = 120)
        {
            return new SkeletonLoader(itemCount, itemHeight);
        }

        #endregion
    }

    #region Supporting Classes

    public class LoadingProgress
    {
        public double Percentage { get; set; }
        public string Message { get; set; }
        public long ProcessedItems { get; set; }
        public long TotalItems { get; set; }
        public TimeSpan? EstimatedTimeRemaining { get; set; }

        public LoadingProgress(double percentage, string message = null)
        {
            Percentage = percentage;
            Message = message;
        }

        public static LoadingProgress FromItems(long processed, long total, string message = null)
        {
            var percentage = total > 0 ? (processed * 100.0 / total) : 0;
            return new LoadingProgress(percentage, message)
            {
                ProcessedItems = processed,
                TotalItems = total
            };
        }
    }

    public class LoadingOverlay : Grid
    {
        private readonly ProgressBar _progressBar;
        private readonly TextBlock _messageBlock;
        private readonly TextBlock _percentageBlock;

        public LoadingOverlay(string message, bool showProgress)
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
            Opacity = 0;

            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(40, 32, 40, 32),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 400,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    Opacity = 0.5,
                    ShadowDepth = 10,
                    BlurRadius = 30
                }
            };

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Spinner animado
            var spinner = CreateSpinner();
            stack.Children.Add(spinner);

            // Mensaje
            _messageBlock = new TextBlock
            {
                Text = message,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 0)
            };
            stack.Children.Add(_messageBlock);

            if (showProgress)
            {
                _progressBar = new ProgressBar
                {
                    Width = 320,
                    Height = 6,
                    Margin = new Thickness(0, 16, 0, 0),
                    Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                    Foreground = new SolidColorBrush(Color.FromRgb(107, 92, 232)),
                    BorderThickness = new Thickness(0),
                    Value = 0,
                    Maximum = 100
                };
                stack.Children.Add(_progressBar);

                _percentageBlock = new TextBlock
                {
                    Text = "0%",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 0)
                };
                stack.Children.Add(_percentageBlock);
            }

            container.Child = stack;
            Children.Add(container);

            // Animar entrada
            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(200)
            };
            BeginAnimation(OpacityProperty, fadeIn);
        }

        public void UpdateProgress(double percentage, string message = null)
        {
            Dispatcher.Invoke(() =>
            {
                if (_progressBar != null)
                {
                    _progressBar.Value = Math.Max(0, Math.Min(100, percentage));
                }

                if (_percentageBlock != null)
                {
                    _percentageBlock.Text = $"{percentage:F0}%";
                }

                if (!string.IsNullOrEmpty(message) && _messageBlock != null)
                {
                    _messageBlock.Text = message;
                }
            });
        }

        public void Close()
        {
            var fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(200)
            };
            fadeOut.Completed += (s, e) =>
            {
                if (Parent is Panel panel)
                {
                    panel.Children.Remove(this);
                }
            };
            BeginAnimation(OpacityProperty, fadeOut);
        }

        private Border CreateSpinner()
        {
            var canvas = new Canvas
            {
                Width = 60,
                Height = 60,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            for (int i = 0; i < 12; i++)
            {
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = 3,
                    Height = 12,
                    Fill = new SolidColorBrush(Color.FromArgb((byte)(255 - i * 15), 107, 92, 232)),
                    RadiusX = 1.5,
                    RadiusY = 1.5
                };

                var angle = i * 30;
                var transform = new TransformGroup();
                transform.Children.Add(new TranslateTransform(28.5, 5));
                transform.Children.Add(new RotateTransform(angle, 30, 30));
                
                rect.RenderTransform = transform;
                canvas.Children.Add(rect);
            }

            var rotateAnimation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(1.2),
                RepeatBehavior = RepeatBehavior.Forever
            };

            var rotateTransform = new RotateTransform(0, 30, 30);
            canvas.RenderTransform = rotateTransform;
            rotateTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnimation);

            return new Border { Child = canvas };
        }
    }

    /// <summary>
    /// Skeleton loader para listas mientras cargan
    /// </summary>
    public class SkeletonLoader : StackPanel
    {
        public SkeletonLoader(int itemCount, double itemHeight)
        {
            Orientation = Orientation.Vertical;
            
            for (int i = 0; i < itemCount; i++)
            {
                var skeleton = CreateSkeletonItem(itemHeight);
                Children.Add(skeleton);
            }
        }

        private Border CreateSkeletonItem(double height)
        {
            var grid = new Grid
            {
                Height = height,
                Margin = new Thickness(8)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(height) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Thumbnail skeleton
            var thumb = new Border
            {
                Width = height * 0.7,
                Height = height * 0.9,
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                CornerRadius = new CornerRadius(8),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(thumb, 0);
            grid.Children.Add(thumb);

            // Content skeleton
            var contentStack = new StackPanel
            {
                Margin = new Thickness(16, 8, 8, 8),
                VerticalAlignment = VerticalAlignment.Center
            };

            var titleBar = new Border
            {
                Height = 18,
                Width = 200,
                Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            contentStack.Children.Add(titleBar);

            var subtitleBar = new Border
            {
                Height = 14,
                Width = 150,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0)
            };
            contentStack.Children.Add(subtitleBar);

            Grid.SetColumn(contentStack, 1);
            grid.Children.Add(contentStack);

            // Animación shimmer
            var shimmer = CreateShimmerAnimation();
            thumb.BeginAnimation(OpacityProperty, shimmer);
            titleBar.BeginAnimation(OpacityProperty, shimmer);
            subtitleBar.BeginAnimation(OpacityProperty, shimmer);

            return new Border
            {
                Child = grid,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0, 0, 0, 12)
            };
        }

        private DoubleAnimation CreateShimmerAnimation()
        {
            return new DoubleAnimation
            {
                From = 0.3,
                To = 1.0,
                Duration = TimeSpan.FromSeconds(1.5),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
        }
    }

    public class LoadingIndicator
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public string Message { get; set; }
        public double Progress { get; set; }
        public bool IsCancellable { get; set; }
        public CancellationTokenSource CancellationSource { get; set; }
    }

    #endregion
}
