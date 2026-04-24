using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ComicReader.Utils;

namespace ComicReader.Services
{
    /// <summary>
    /// Servicio para manejar gestos táctiles en pantallas touch
    /// Soporta: Swipe, Pinch-to-Zoom, Double-Tap, Long-Press
    /// </summary>
    public sealed class TouchGestureService
    {
        private static readonly Lazy<TouchGestureService> _instance = 
            new Lazy<TouchGestureService>(() => new TouchGestureService());
        
        public static TouchGestureService Instance => _instance.Value;

        // Umbrales de detección
        private const double SwipeThreshold = 100;  // píxeles
        private const double PinchThreshold = 0.1;   // 10% de cambio
        private const int DoubleTapMaxDelay = 500;   // milisegundos
        private const int LongPressDelay = 800;      // milisegundos

        // Estado de gestos
        private Point _touchStartPoint;
        private Point _touch2StartPoint;
        private DateTime _lastTapTime = DateTime.MinValue;
        private DateTime _touchStartTime;
        private bool _isTwoFingerTouch;
        private double _initialPinchDistance;
        private System.Windows.Threading.DispatcherTimer _longPressTimer;

        // Eventos
        public event Action<SwipeDirection> SwipeDetected;
        public event Action<double> PinchZoom; // Factor de zoom (1.0 = sin cambio)
        public event Action<Point> DoubleTap;
        public event Action<Point> LongPress;
        public event Action<Point> SingleTap;

        public enum SwipeDirection
        {
            Left,
            Right,
            Up,
            Down
        }

        private TouchGestureService()
        {
            _longPressTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(LongPressDelay)
            };
            _longPressTimer.Tick += LongPressTimer_Tick;
            
            ModernLogger.Info("✓ TouchGestureService inicializado");
        }

        /// <summary>
        /// Registra un elemento para gestos táctiles
        /// </summary>
        public void RegisterElement(FrameworkElement element)
        {
            if (element == null) return;

            // Touch events
            element.TouchDown += Element_TouchDown;
            element.TouchMove += Element_TouchMove;
            element.TouchUp += Element_TouchUp;
            
            // Mouse events como fallback
            element.MouseLeftButtonDown += Element_MouseLeftButtonDown;
            element.MouseMove += Element_MouseMove;
            element.MouseLeftButtonUp += Element_MouseLeftButtonUp;
            
            // Manipulation events para pinch
            element.IsManipulationEnabled = true;
            element.ManipulationStarting += Element_ManipulationStarting;
            element.ManipulationDelta += Element_ManipulationDelta;
            element.ManipulationCompleted += Element_ManipulationCompleted;

            ModernLogger.Info($"✓ Elemento registrado para gestos: {element.GetType().Name}");
        }

        /// <summary>
        /// Des-registra un elemento
        /// </summary>
        public void UnregisterElement(FrameworkElement element)
        {
            if (element == null) return;

            element.TouchDown -= Element_TouchDown;
            element.TouchMove -= Element_TouchMove;
            element.TouchUp -= Element_TouchUp;
            element.MouseLeftButtonDown -= Element_MouseLeftButtonDown;
            element.MouseMove -= Element_MouseMove;
            element.MouseLeftButtonUp -= Element_MouseLeftButtonUp;
            element.ManipulationStarting -= Element_ManipulationStarting;
            element.ManipulationDelta -= Element_ManipulationDelta;
            element.ManipulationCompleted -= Element_ManipulationCompleted;
        }

        // ============================================================
        // TOUCH EVENTS
        // ============================================================

        private void Element_TouchDown(object sender, TouchEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element == null) return;

            var touchPoint = e.GetTouchPoint(element);
            _touchStartPoint = touchPoint.Position;
            _touchStartTime = DateTime.Now;
            
            // Detectar multi-touch
            var touchDevice = e.TouchDevice;
            var allTouches = element.TouchesOver.ToList();
            
            if (allTouches.Count >= 2)
            {
                _isTwoFingerTouch = true;
                _touch2StartPoint = allTouches[1].GetTouchPoint(element).Position;
                _initialPinchDistance = GetDistance(_touchStartPoint, _touch2StartPoint);
            }
            else
            {
                _isTwoFingerTouch = false;
                
                // Iniciar timer para long press
                _longPressTimer.Stop();
                _longPressTimer.Start();
            }
        }

        private void Element_TouchMove(object sender, TouchEventArgs e)
        {
            // Cancelar long press si hay movimiento
            if (_longPressTimer.IsEnabled)
            {
                var element = sender as FrameworkElement;
                var currentPoint = e.GetTouchPoint(element).Position;
                var distance = GetDistance(_touchStartPoint, currentPoint);
                
                if (distance > 10) // 10 píxeles de tolerancia
                {
                    _longPressTimer.Stop();
                }
            }
        }

        private void Element_TouchUp(object sender, TouchEventArgs e)
        {
            _longPressTimer.Stop();
            
            if (_isTwoFingerTouch)
            {
                _isTwoFingerTouch = false;
                return;
            }

            var element = sender as FrameworkElement;
            var endPoint = e.GetTouchPoint(element).Position;
            var timeDiff = (DateTime.Now - _touchStartTime).TotalMilliseconds;
            
            // Detectar swipe
            var deltaX = endPoint.X - _touchStartPoint.X;
            var deltaY = endPoint.Y - _touchStartPoint.Y;
            var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            
            if (distance > SwipeThreshold && timeDiff < 500)
            {
                // Es un swipe
                SwipeDirection direction;
                
                if (Math.Abs(deltaX) > Math.Abs(deltaY))
                {
                    direction = deltaX > 0 ? SwipeDirection.Right : SwipeDirection.Left;
                }
                else
                {
                    direction = deltaY > 0 ? SwipeDirection.Down : SwipeDirection.Up;
                }
                
                ModernLogger.Info($"👆 Swipe detectado: {direction}");
                SwipeDetected?.Invoke(direction);
            }
            else if (distance < 10 && timeDiff < 300)
            {
                // Es un tap
                var now = DateTime.Now;
                var timeSinceLastTap = (now - _lastTapTime).TotalMilliseconds;
                
                if (timeSinceLastTap < DoubleTapMaxDelay)
                {
                    // Double tap
                    ModernLogger.Info($"👆👆 Double tap detectado");
                    DoubleTap?.Invoke(endPoint);
                    _lastTapTime = DateTime.MinValue; // Reset
                }
                else
                {
                    // Single tap (esperar para confirmar que no es double)
                    _lastTapTime = now;
                    var delayTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(DoubleTapMaxDelay)
                    };
                    delayTimer.Tick += (s, args) =>
                    {
                        delayTimer.Stop();
                        if ((DateTime.Now - _lastTapTime).TotalMilliseconds >= DoubleTapMaxDelay - 10)
                        {
                            ModernLogger.Info($"👆 Single tap detectado");
                            SingleTap?.Invoke(endPoint);
                        }
                    };
                    delayTimer.Start();
                }
            }
        }

        // ============================================================
        // MOUSE EVENTS (Fallback)
        // ============================================================

        private void Element_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var element = sender as FrameworkElement;
            _touchStartPoint = e.GetPosition(element);
            _touchStartTime = DateTime.Now;
            
            _longPressTimer.Stop();
            _longPressTimer.Start();
        }

        private void Element_MouseMove(object sender, MouseEventArgs e)
        {
            if (_longPressTimer.IsEnabled)
            {
                var element = sender as FrameworkElement;
                var currentPoint = e.GetPosition(element);
                var distance = GetDistance(_touchStartPoint, currentPoint);
                
                if (distance > 10)
                {
                    _longPressTimer.Stop();
                }
            }
        }

        private void Element_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _longPressTimer.Stop();
            
            var element = sender as FrameworkElement;
            var endPoint = e.GetPosition(element);
            var timeDiff = (DateTime.Now - _touchStartTime).TotalMilliseconds;
            
            var distance = GetDistance(_touchStartPoint, endPoint);
            
            if (distance > SwipeThreshold && timeDiff < 500)
            {
                var deltaX = endPoint.X - _touchStartPoint.X;
                var deltaY = endPoint.Y - _touchStartPoint.Y;
                
                SwipeDirection direction;
                if (Math.Abs(deltaX) > Math.Abs(deltaY))
                {
                    direction = deltaX > 0 ? SwipeDirection.Right : SwipeDirection.Left;
                }
                else
                {
                    direction = deltaY > 0 ? SwipeDirection.Down : SwipeDirection.Up;
                }
                
                SwipeDetected?.Invoke(direction);
            }
        }

        // ============================================================
        // MANIPULATION EVENTS (Pinch)
        // ============================================================

        private void Element_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            e.ManipulationContainer = sender as FrameworkElement;
            e.Mode = ManipulationModes.Scale | ManipulationModes.Translate;
        }

        private void Element_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            var scale = e.DeltaManipulation.Scale;
            
            if (Math.Abs(scale.X - 1.0) > 0.01 || Math.Abs(scale.Y - 1.0) > 0.01)
            {
                // Pinch detectado
                var avgScale = (scale.X + scale.Y) / 2.0;
                ModernLogger.Info($"🤏 Pinch zoom: {avgScale:F2}x");
                PinchZoom?.Invoke(avgScale);
            }
        }

        private void Element_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
        {
            // Manipulación completada
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private void LongPressTimer_Tick(object sender, EventArgs e)
        {
            _longPressTimer.Stop();
            ModernLogger.Info($"👆⏱ Long press detectado");
            LongPress?.Invoke(_touchStartPoint);
        }

        private double GetDistance(Point p1, Point p2)
        {
            var dx = p2.X - p1.X;
            var dy = p2.Y - p1.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Resetea el estado del servicio
        /// </summary>
        public void Reset()
        {
            _longPressTimer.Stop();
            _isTwoFingerTouch = false;
            _lastTapTime = DateTime.MinValue;
        }
    }
}
