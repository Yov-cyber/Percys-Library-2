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
                
                // Posicionar. ActualHeight es 0 antes del primer measure;
                // usar la misma altura nominal que RepositionToasts (95)
                // para evitar que el primer toast aparezca sobre los previos.
                var yPosition = TopMargin + _activeToasts.Count * (NotificationToast.NominalHeight + ToastSpacing);
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
                var targetY = TopMargin + i * (NotificationToast.NominalHeight + ToastSpacing);

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
        // Altura nominal usada por NotificationService para apilar toasts
        // antes de que ocurra el primer measure (ActualHeight=0).
        public const double NominalHeight = 78;

        private readonly TextBlock _titleBlock;
        private readonly TextBlock _messageBlock;
        private readonly ProgressBar _progressBar;

        // Construye el toast con el sistema de tokens del proyecto. Si los
        // recursos no estan disponibles (test/design-time), usa fallbacks
        // hexa de los mismos valores definidos en Tokens.xaml.
        public NotificationToast(string message, string title, NotificationType type, double? progress = null)
        {
            Width = 380;
            MinHeight = 70;
            CornerRadius = new CornerRadius(6);
            Background = ResolveBrush("Surface.Raised.Brush", "#16181D");
            BorderBrush = ResolveBrush("Border.Subtle.Brush", "#262932");
            BorderThickness = new Thickness(1);
            Padding = new Thickness(14, 12, 14, 12);
            Cursor = System.Windows.Input.Cursors.Hand;
            Opacity = 0;

            // Sombra discreta. El diseno previo (Opacity 0.4, BlurRadius 20)
            // dejaba un halo pesado que rompia la regla de "sin efectos
            // decorativos".
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.35,
                ShadowDepth = 2,
                BlurRadius = 16
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Indicador vertical 3px en el color del kind. Sustituye al
            // circulo amarillo/verde/rojo del diseno previo.
            var kindIndicator = new Border
            {
                Width = 3,
                Height = 32,
                CornerRadius = new CornerRadius(2),
                Background = GetIconBackground(type),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(kindIndicator, 0);
            grid.Children.Add(kindIndicator);

            var contentStack = new StackPanel
            {
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            _titleBlock = new TextBlock
            {
                Text = title,
                FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = ResolveBrush("Text.Primary.Brush", "#ECEDEE"),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            _messageBlock = new TextBlock
            {
                Text = message,
                FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
                FontSize = 12,
                Foreground = ResolveBrush("Text.Secondary.Brush", "#9BA1A8"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
                MaxHeight = 60
            };

            contentStack.Children.Add(_titleBlock);
            contentStack.Children.Add(_messageBlock);

            if (type == NotificationType.Progress)
            {
                _progressBar = new ProgressBar
                {
                    Height = 2,
                    Margin = new Thickness(0, 8, 0, 0),
                    Background = ResolveBrush("Border.Subtle.Brush", "#262932"),
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

        // Color del indicador segun el kind. Resuelve desde tokens; los
        // valores hexa son los mismos que en Tokens.xaml para que el
        // fallback no introduzca un color nuevo.
        private Brush GetIconBackground(NotificationType type)
        {
            return type switch
            {
                NotificationType.Success => ResolveBrush("Success.Brush", "#10B981"),
                NotificationType.Info => ResolveBrush("Accent.Brush", "#3B82F6"),
                NotificationType.Warning => ResolveBrush("Warning.Brush", "#F59E0B"),
                NotificationType.Error => ResolveBrush("Danger.Brush", "#EF4444"),
                NotificationType.Progress => ResolveBrush("Accent.Brush", "#3B82F6"),
                _ => ResolveBrush("Border.Strong.Brush", "#3A3F4A")
            };
        }

        private static Brush ResolveBrush(string resourceKey, string fallbackHex)
        {
            try
            {
                var b = Application.Current?.TryFindResource(resourceKey) as Brush;
                if (b != null) return b;
            }
            catch { }
            try { return (Brush)new BrushConverter().ConvertFromString(fallbackHex); }
            catch { return Brushes.Gray; }
        }
    }

    #endregion
}
