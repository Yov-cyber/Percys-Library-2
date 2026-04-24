// FileName: Services/Notifications/NotificationService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ComicReader.Services.Notifications
{
    /// <summary>
    /// Sistema de notificaciones premium con toast modernas, animaciones fluidas y stack management
    /// </summary>
    public class NotificationService
    {
        private static NotificationService _instance;
        private readonly List<NotificationToast> _activeToasts = new List<NotificationToast>();
        private Window _mainWindow;
        private Canvas _toastContainer;
        private const int MaxToasts = 5;
        private const double ToastWidth = 380;
        private const double ToastSpacing = 12;
        private const double RightMargin = 24;
        private const double TopMargin = 80;

        public static NotificationService Instance => _instance ??= new NotificationService();

        private NotificationService() { }

        /// <summary>
        /// Inicializar el servicio con la ventana principal
        /// </summary>
        public void Initialize(Window mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            
            // Crear contenedor de toasts si no existe
            if (_toastContainer == null)
            {
                _toastContainer = new Canvas
                {
                    IsHitTestVisible = false,
                    ClipToBounds = false
                };

                // Agregar al contenedor principal
                if (_mainWindow.Content is Grid mainGrid)
                {
                    mainGrid.Children.Add(_toastContainer);
                    Panel.SetZIndex(_toastContainer, 9999);
                }
                else if (_mainWindow.Content is Panel panel)
                {
                    panel.Children.Add(_toastContainer);
                    Panel.SetZIndex(_toastContainer, 9999);
                }
            }
        }

        #region Public API

        /// <summary>
        /// Muestra una notificación de éxito
        /// </summary>
        public void Success(string message, string title = "Éxito", int durationMs = 4000)
        {
            ShowToast(message, title, NotificationType.Success, durationMs);
        }

        /// <summary>
        /// Muestra una notificación informativa
        /// </summary>
        public void Info(string message, string title = "Información", int durationMs = 3000)
        {
            ShowToast(message, title, NotificationType.Info, durationMs);
        }

        /// <summary>
        /// Muestra una advertencia
        /// </summary>
        public void Warning(string message, string title = "Advertencia", int durationMs = 5000)
        {
            ShowToast(message, title, NotificationType.Warning, durationMs);
        }

        /// <summary>
        /// Muestra un error
        /// </summary>
        public void Error(string message, string title = "Error", int durationMs = 6000)
        {
            ShowToast(message, title, NotificationType.Error, durationMs);
        }

        /// <summary>
        /// Muestra un progreso (sin auto-dismiss)
        /// </summary>
        public NotificationToast Progress(string message, string title = "Procesando...", double? progress = null)
        {
            return ShowToast(message, title, NotificationType.Progress, -1, progress);
        }

        /// <summary>
        /// Actualiza un toast de progreso existente
        /// </summary>
        public void UpdateProgress(NotificationToast toast, double progress, string message = null)
        {
            if (toast == null || !_activeToasts.Contains(toast)) return;

            _mainWindow?.Dispatcher.Invoke(() =>
            {
                toast.UpdateProgress(progress, message);
            });
        }

        /// <summary>
        /// Cierra un toast específico
        /// </summary>
        public void Close(NotificationToast toast)
        {
            if (toast == null) return;
            DismissToast(toast);
        }

        /// <summary>
        /// Cierra todos los toasts
        /// </summary>
        public void CloseAll()
        {
            var toasts = _activeToasts.ToList();
            foreach (var toast in toasts)
            {
                DismissToast(toast);
            }
        }

        #endregion

        #region Private Methods

        private NotificationToast ShowToast(string message, string title, NotificationType type, int durationMs, double? progress = null)
        {
            if (_mainWindow == null || _toastContainer == null)
            {
                // Fallback a MessageBox si no está inicializado
                MessageBox.Show(message, title, MessageBoxButton.OK, GetMessageBoxImage(type));
                return null;
            }

            NotificationToast toast = null;

            _mainWindow.Dispatcher.Invoke(() =>
            {
                // Limitar número de toasts
                if (_activeToasts.Count >= MaxToasts)
                {
                    DismissToast(_activeToasts[0]);
                }

                // Crear toast
                toast = new NotificationToast(message, title, type, progress);
                toast.IsHitTestVisible = true;
                
                // Posicionar
                var yPosition = TopMargin + _activeToasts.Count * (toast.ActualHeight + ToastSpacing);
                Canvas.SetRight(toast, RightMargin);
                Canvas.SetTop(toast, yPosition);

                // Agregar al contenedor
                _toastContainer.Children.Add(toast);
                _activeToasts.Add(toast);

                // Reposicionar otros toasts
                RepositionToasts();

                // Animar entrada
                AnimateIn(toast);

                // Auto-dismiss si tiene duración
                if (durationMs > 0)
                {
                    var timer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(durationMs)
                    };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        DismissToast(toast);
                    };
                    timer.Start();
                }

                // Evento de click para cerrar
                toast.MouseLeftButtonDown += (s, e) =>
                {
                    DismissToast(toast);
                };
            });

            return toast;
        }

        private void DismissToast(NotificationToast toast)
        {
            if (toast == null || !_activeToasts.Contains(toast)) return;

            _mainWindow?.Dispatcher.Invoke(() =>
            {
                AnimateOut(toast, () =>
                {
                    _activeToasts.Remove(toast);
                    _toastContainer.Children.Remove(toast);
                    RepositionToasts();
                });
            });
        }

        private void RepositionToasts()
        {
            for (int i = 0; i < _activeToasts.Count; i++)
            {
                var toast = _activeToasts[i];
                var targetY = TopMargin + i * (95 + ToastSpacing);

                var animation = new DoubleAnimation
                {
                    To = targetY,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                toast.BeginAnimation(Canvas.TopProperty, animation);
            }
        }

        private void AnimateIn(NotificationToast toast)
        {
            // Slide in from right + fade in
            var slideAnimation = new DoubleAnimation
            {
                From = -400,
                To = RightMargin,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 }
            };

            var fadeAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300)
            };

            toast.BeginAnimation(Canvas.RightProperty, slideAnimation);
            toast.BeginAnimation(UIElement.OpacityProperty, fadeAnimation);
        }

        private void AnimateOut(NotificationToast toast, Action onComplete)
        {
            // Slide out to right + fade out
            var slideAnimation = new DoubleAnimation
            {
                From = RightMargin,
                To = -400,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            var fadeAnimation = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(250)
            };

            slideAnimation.Completed += (s, e) => onComplete?.Invoke();

            toast.BeginAnimation(Canvas.RightProperty, slideAnimation);
            toast.BeginAnimation(UIElement.OpacityProperty, fadeAnimation);
        }

        private MessageBoxImage GetMessageBoxImage(NotificationType type)
        {
            return type switch
            {
                NotificationType.Success => MessageBoxImage.Information,
                NotificationType.Info => MessageBoxImage.Information,
                NotificationType.Warning => MessageBoxImage.Warning,
                NotificationType.Error => MessageBoxImage.Error,
                _ => MessageBoxImage.None
            };
        }

        #endregion
    }

    #region Supporting Classes

    public enum NotificationType
    {
        Success,
        Info,
        Warning,
        Error,
        Progress
    }

    public class NotificationToast : Border
    {
        private readonly TextBlock _titleBlock;
        private readonly TextBlock _messageBlock;
        private readonly ProgressBar _progressBar;
        private readonly Border _iconContainer;
        private readonly TextBlock _iconText;

        public NotificationToast(string message, string title, NotificationType type, double? progress = null)
        {
            // Configuración del border principal
            Width = 380;
            MinHeight = 85;
            CornerRadius = new CornerRadius(12);
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            BorderBrush = GetBorderColor(type);
            BorderThickness = new Thickness(0, 0, 0, 3);
            Padding = new Thickness(16, 14, 16, 14);
            Cursor = System.Windows.Input.Cursors.Hand;
            Opacity = 0;

            // Sombra
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.4,
                ShadowDepth = 8,
                BlurRadius = 20
            };

            // Layout interno
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Ícono
            _iconContainer = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                Background = GetIconBackground(type),
                VerticalAlignment = VerticalAlignment.Top
            };

            _iconText = new TextBlock
            {
                Text = GetIcon(type),
                FontSize = 20,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold
            };

            _iconContainer.Child = _iconText;
            Grid.SetColumn(_iconContainer, 0);
            grid.Children.Add(_iconContainer);

            // Contenido
            var contentStack = new StackPanel
            {
                Margin = new Thickness(12, 0, 0, 0)
            };

            _titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            _messageBlock = new TextBlock
            {
                Text = message,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
                MaxHeight = 60
            };

            contentStack.Children.Add(_titleBlock);
            contentStack.Children.Add(_messageBlock);

            // Progress bar si es necesario
            if (type == NotificationType.Progress)
            {
                _progressBar = new ProgressBar
                {
                    Height = 4,
                    Margin = new Thickness(0, 8, 0, 0),
                    Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                    Foreground = GetIconBackground(type),
                    BorderThickness = new Thickness(0)
                };

                if (progress.HasValue)
                {
                    _progressBar.Value = progress.Value;
                    _progressBar.IsIndeterminate = false;
                }
                else
                {
                    _progressBar.IsIndeterminate = true;
                }

                contentStack.Children.Add(_progressBar);
            }

            Grid.SetColumn(contentStack, 1);
            grid.Children.Add(contentStack);

            Child = grid;
        }

        public void UpdateProgress(double progress, string message = null)
        {
            if (_progressBar != null)
            {
                _progressBar.IsIndeterminate = false;
                _progressBar.Value = Math.Max(0, Math.Min(100, progress));
            }

            if (!string.IsNullOrEmpty(message) && _messageBlock != null)
            {
                _messageBlock.Text = message;
            }
        }

        private Brush GetIconBackground(NotificationType type)
        {
            return type switch
            {
                NotificationType.Success => new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                NotificationType.Info => new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                NotificationType.Warning => new SolidColorBrush(Color.FromRgb(251, 146, 60)),
                NotificationType.Error => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                NotificationType.Progress => new SolidColorBrush(Color.FromRgb(168, 85, 247)),
                _ => new SolidColorBrush(Color.FromRgb(100, 100, 100))
            };
        }

        private Brush GetBorderColor(NotificationType type)
        {
            return type switch
            {
                NotificationType.Success => new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                NotificationType.Info => new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                NotificationType.Warning => new SolidColorBrush(Color.FromRgb(251, 146, 60)),
                NotificationType.Error => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                NotificationType.Progress => new SolidColorBrush(Color.FromRgb(168, 85, 247)),
                _ => new SolidColorBrush(Color.FromRgb(100, 100, 100))
            };
        }

        private string GetIcon(NotificationType type)
        {
            return type switch
            {
                NotificationType.Success => "✓",
                NotificationType.Info => "ℹ",
                NotificationType.Warning => "⚠",
                NotificationType.Error => "✕",
                NotificationType.Progress => "⟳",
                _ => "•"
            };
        }
    }

    #endregion
}
