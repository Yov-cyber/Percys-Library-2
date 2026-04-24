using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ComicReader.Utils;

namespace ComicReader.Services
{
    /// <summary>
    /// Servicio para modo de lectura inmersivo
    /// Oculta automáticamente la UI y la muestra al mover el mouse
    /// </summary>
    public sealed class ImmersiveReadingService
    {
        private static readonly Lazy<ImmersiveReadingService> _instance = 
            new Lazy<ImmersiveReadingService>(() => new ImmersiveReadingService());
        
        public static ImmersiveReadingService Instance => _instance.Value;

        private DispatcherTimer _hideTimer;
        private Point _lastMousePosition;
        private bool _isImmersiveMode;
        private bool _isUiVisible = true;

        // Configuración
        public int HideDelayMs { get; set; } = 3000; // 3 segundos sin movimiento
        public double MouseMovementThreshold { get; set; } = 5; // píxeles
        public TimeSpan AnimationDuration { get; set; } = TimeSpan.FromMilliseconds(300);

        // Elementos UI a controlar
        private UIElement[] _uiElements;
        private Window _parentWindow;

        // Eventos
        public event Action UiHidden;
        public event Action UiShown;

        private ImmersiveReadingService()
        {
            _hideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(HideDelayMs)
            };
            _hideTimer.Tick += HideTimer_Tick;

            ModernLogger.Info("✓ ImmersiveReadingService inicializado");
        }

        /// <summary>
        /// Activa el modo inmersivo
        /// </summary>
        public void EnableImmersiveMode(Window window, params UIElement[] uiElements)
        {
            if (_isImmersiveMode) return;

            _parentWindow = window;
            _uiElements = uiElements;
            _isImmersiveMode = true;

            // Suscribirse a eventos del mouse
            window.MouseMove += Window_MouseMove;
            window.PreviewMouseMove += Window_PreviewMouseMove;
            window.MouseLeave += Window_MouseLeave;

            // Iniciar timer
            _hideTimer.Interval = TimeSpan.FromMilliseconds(HideDelayMs);
            _hideTimer.Start();

            ModernLogger.Info($"✓ Modo inmersivo activado para {uiElements.Length} elementos");
        }

        /// <summary>
        /// Desactiva el modo inmersivo
        /// </summary>
        public void DisableImmersiveMode()
        {
            if (!_isImmersiveMode) return;

            _hideTimer.Stop();
            
            if (_parentWindow != null)
            {
                _parentWindow.MouseMove -= Window_MouseMove;
                _parentWindow.PreviewMouseMove -= Window_PreviewMouseMove;
                _parentWindow.MouseLeave -= Window_MouseLeave;
            }

            // Mostrar UI
            ShowUi(instant: true);

            _isImmersiveMode = false;
            _parentWindow = null;
            _uiElements = null;

            ModernLogger.Info("✓ Modo inmersivo desactivado");
        }

        /// <summary>
        /// Alterna el modo inmersivo
        /// </summary>
        public void ToggleImmersiveMode(Window window, params UIElement[] uiElements)
        {
            if (_isImmersiveMode)
            {
                DisableImmersiveMode();
            }
            else
            {
                EnableImmersiveMode(window, uiElements);
            }
        }

        // ============================================================
        // EVENTOS
        // ============================================================

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isImmersiveMode) return;

            var currentPos = e.GetPosition(_parentWindow);
            var distance = GetDistance(_lastMousePosition, currentPos);

            if (distance > MouseMovementThreshold)
            {
                // Movimiento significativo detectado
                _lastMousePosition = currentPos;

                if (!_isUiVisible)
                {
                    ShowUi();
                }

                // Reiniciar timer
                _hideTimer.Stop();
                _hideTimer.Start();
            }
        }

        private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            // Asegurar que capturamos todos los movimientos
            Window_MouseMove(sender, e);
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            // Mouse salió de la ventana, ocultar UI inmediatamente
            if (_isImmersiveMode && _isUiVisible)
            {
                _hideTimer.Stop();
                HideUi();
            }
        }

        private void HideTimer_Tick(object sender, EventArgs e)
        {
            _hideTimer.Stop();
            
            if (_isImmersiveMode && _isUiVisible)
            {
                HideUi();
            }
        }

        // ============================================================
        // UI CONTROL
        // ============================================================

        private void ShowUi(bool instant = false)
        {
            if (_uiElements == null || _isUiVisible) return;

            _isUiVisible = true;

            foreach (var element in _uiElements)
            {
                if (element == null) continue;

                if (instant)
                {
                    element.Visibility = Visibility.Visible;
                    element.Opacity = 1.0;
                }
                else
                {
                    element.Visibility = Visibility.Visible;
                    
                    var fadeIn = new DoubleAnimation
                    {
                        From = element.Opacity,
                        To = 1.0,
                        Duration = AnimationDuration,
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    element.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                }
            }

            // Mostrar cursor
            if (_parentWindow != null)
            {
                _parentWindow.Cursor = Cursors.Arrow;
            }

            ModernLogger.Info("👁 UI mostrada");
            UiShown?.Invoke();
        }

        private void HideUi(bool instant = false)
        {
            if (_uiElements == null || !_isUiVisible) return;

            _isUiVisible = false;

            foreach (var element in _uiElements)
            {
                if (element == null) continue;

                if (instant)
                {
                    element.Visibility = Visibility.Collapsed;
                    element.Opacity = 0.0;
                }
                else
                {
                    var fadeOut = new DoubleAnimation
                    {
                        From = element.Opacity,
                        To = 0.0,
                        Duration = AnimationDuration,
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                    };

                    fadeOut.Completed += (s, e) =>
                    {
                        element.Visibility = Visibility.Collapsed;
                    };

                    element.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                }
            }

            // Ocultar cursor
            if (_parentWindow != null)
            {
                _parentWindow.Cursor = Cursors.None;
            }

            ModernLogger.Info("👁 UI ocultada");
            UiHidden?.Invoke();
        }

        /// <summary>
        /// Fuerza mostrar la UI temporalmente
        /// </summary>
        public void ShowUiTemporarily(int durationMs = 5000)
        {
            if (!_isImmersiveMode) return;

            ShowUi();
            
            _hideTimer.Stop();
            _hideTimer.Interval = TimeSpan.FromMilliseconds(durationMs);
            _hideTimer.Start();
        }

        /// <summary>
        /// Fuerza ocultar la UI
        /// </summary>
        public void ForceHideUi()
        {
            if (!_isImmersiveMode) return;

            _hideTimer.Stop();
            HideUi();
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private double GetDistance(Point p1, Point p2)
        {
            var dx = p2.X - p1.X;
            var dy = p2.Y - p1.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public bool IsImmersiveMode => _isImmersiveMode;
        public bool IsUiVisible => _isUiVisible;

        /// <summary>
        /// Configuración rápida con valores predefinidos
        /// </summary>
        public void ConfigureQuick(ImmersivePreset preset)
        {
            switch (preset)
            {
                case ImmersivePreset.Minimal:
                    HideDelayMs = 2000;
                    AnimationDuration = TimeSpan.FromMilliseconds(200);
                    break;
                
                case ImmersivePreset.Standard:
                    HideDelayMs = 3000;
                    AnimationDuration = TimeSpan.FromMilliseconds(300);
                    break;
                
                case ImmersivePreset.Relaxed:
                    HideDelayMs = 5000;
                    AnimationDuration = TimeSpan.FromMilliseconds(400);
                    break;
            }
        }

        public enum ImmersivePreset
        {
            Minimal,    // Oculta rápido (2s)
            Standard,   // Oculta normal (3s)
            Relaxed     // Oculta lento (5s)
        }
    }
}
