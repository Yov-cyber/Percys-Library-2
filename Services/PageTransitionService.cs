using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ComicReader.Utils;

namespace ComicReader.Services
{
    /// <summary>
    /// Servicio para gestionar transiciones suaves entre páginas del cómic
    /// Incluye múltiples efectos: Fade, Slide, Zoom, Flip
    /// </summary>
    public sealed class PageTransitionService
    {
        private static readonly Lazy<PageTransitionService> _instance = 
            new Lazy<PageTransitionService>(() => new PageTransitionService());
        
        public static PageTransitionService Instance => _instance.Value;

        public enum TransitionType
        {
            None,
            Fade,
            SlideLeft,
            SlideRight,
            ZoomIn,
            ZoomOut,
            Flip
        }

        private TransitionType _currentTransitionType = TransitionType.Fade;
        private double _transitionDurationMs = 300;
        private bool _animationsEnabled = true;

        private PageTransitionService()
        {
            // Leer configuración de animaciones
            try
            {
                _animationsEnabled = SettingsManager.Settings?.AnimationsEnabled ?? true;
            }
            catch { }
            
            ModernLogger.Info($"✓ PageTransitionService inicializado (Animaciones: {(_animationsEnabled ? "ON" : "OFF")})");
        }

        /// <summary>
        /// Configura el tipo de transición
        /// </summary>
        public void SetTransitionType(TransitionType type)
        {
            _currentTransitionType = type;
            ModernLogger.Info($"🎬 Transición configurada: {type}");
        }

        /// <summary>
        /// Configura la duración de la transición en milisegundos
        /// </summary>
        public void SetDuration(double durationMs)
        {
            _transitionDurationMs = Math.Max(50, Math.Min(2000, durationMs));
        }

        /// <summary>
        /// Aplica transición de salida a la imagen actual
        /// </summary>
        public void ApplyTransitionOut(Image image, Action onComplete = null)
        {
            if (!_animationsEnabled || _currentTransitionType == TransitionType.None)
            {
                onComplete?.Invoke();
                return;
            }

            try
            {
                var duration = TimeSpan.FromMilliseconds(_transitionDurationMs);
                
                switch (_currentTransitionType)
                {
                    case TransitionType.Fade:
                        ApplyFadeOut(image, duration, onComplete);
                        break;
                    
                    case TransitionType.SlideLeft:
                        ApplySlideOut(image, duration, -1, onComplete);
                        break;
                    
                    case TransitionType.SlideRight:
                        ApplySlideOut(image, duration, 1, onComplete);
                        break;
                    
                    case TransitionType.ZoomOut:
                        ApplyZoomOutTransition(image, duration, onComplete);
                        break;
                    
                    default:
                        onComplete?.Invoke();
                        break;
                }
            }
            catch (Exception ex)
            {
                DevLogger.Error($"Error en transición de salida: {ex.Message}");
                onComplete?.Invoke();
            }
        }

        /// <summary>
        /// Aplica transición de entrada a la nueva imagen
        /// </summary>
        public void ApplyTransitionIn(Image image, Action onComplete = null)
        {
            if (!_animationsEnabled || _currentTransitionType == TransitionType.None)
            {
                image.Opacity = 1.0;
                onComplete?.Invoke();
                return;
            }

            try
            {
                var duration = TimeSpan.FromMilliseconds(_transitionDurationMs);
                
                switch (_currentTransitionType)
                {
                    case TransitionType.Fade:
                        ApplyFadeIn(image, duration, onComplete);
                        break;
                    
                    case TransitionType.SlideLeft:
                        ApplySlideIn(image, duration, -1, onComplete);
                        break;
                    
                    case TransitionType.SlideRight:
                        ApplySlideIn(image, duration, 1, onComplete);
                        break;
                    
                    case TransitionType.ZoomIn:
                        ApplyZoomInTransition(image, duration, onComplete);
                        break;
                    
                    default:
                        image.Opacity = 1.0;
                        onComplete?.Invoke();
                        break;
                }
            }
            catch (Exception ex)
            {
                DevLogger.Error($"Error en transición de entrada: {ex.Message}");
                image.Opacity = 1.0;
                onComplete?.Invoke();
            }
        }

        // ============================================================
        // EFECTOS INDIVIDUALES
        // ============================================================

        private void ApplyFadeOut(Image image, TimeSpan duration, Action onComplete)
        {
            var animation = new DoubleAnimation
            {
                From = image.Opacity,
                To = 0.0,
                Duration = duration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            
            animation.Completed += (s, e) => onComplete?.Invoke();
            image.BeginAnimation(UIElement.OpacityProperty, animation);
        }

        private void ApplyFadeIn(Image image, TimeSpan duration, Action onComplete)
        {
            image.Opacity = 0.0;
            
            var animation = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = duration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            
            animation.Completed += (s, e) => onComplete?.Invoke();
            image.BeginAnimation(UIElement.OpacityProperty, animation);
        }

        private void ApplySlideOut(Image image, TimeSpan duration, int direction, Action onComplete)
        {
            EnsureRenderTransform(image);
            var transform = image.RenderTransform as TranslateTransform;
            
            var animation = new DoubleAnimation
            {
                From = 0,
                To = direction * image.ActualWidth,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            
            animation.Completed += (s, e) => onComplete?.Invoke();
            transform?.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        private void ApplySlideIn(Image image, TimeSpan duration, int direction, Action onComplete)
        {
            EnsureRenderTransform(image);
            var transform = image.RenderTransform as TranslateTransform;
            
            if (transform != null)
            {
                transform.X = -direction * image.ActualWidth;
            }
            
            image.Opacity = 1.0;
            
            var animation = new DoubleAnimation
            {
                From = -direction * image.ActualWidth,
                To = 0,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            animation.Completed += (s, e) => onComplete?.Invoke();
            transform?.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        private void ApplyZoomOutTransition(Image image, TimeSpan duration, Action onComplete)
        {
            EnsureScaleTransform(image);
            
            var scaleAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.8,
                Duration = duration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            
            var opacityAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = duration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            
            opacityAnimation.Completed += (s, e) => onComplete?.Invoke();
            
            var transform = image.RenderTransform as ScaleTransform;
            transform?.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            transform?.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
            image.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
        }

        private void ApplyZoomInTransition(Image image, TimeSpan duration, Action onComplete)
        {
            EnsureScaleTransform(image);
            
            var transform = image.RenderTransform as ScaleTransform;
            if (transform != null)
            {
                transform.ScaleX = 0.8;
                transform.ScaleY = 0.8;
            }
            
            image.Opacity = 0.0;
            
            var scaleAnimation = new DoubleAnimation
            {
                From = 0.8,
                To = 1.0,
                Duration = duration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            
            var opacityAnimation = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = duration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            
            opacityAnimation.Completed += (s, e) => onComplete?.Invoke();
            
            transform?.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            transform?.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
            image.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private void EnsureRenderTransform(Image image)
        {
            if (image.RenderTransform == null || !(image.RenderTransform is TranslateTransform))
            {
                image.RenderTransform = new TranslateTransform();
                image.RenderTransformOrigin = new Point(0.5, 0.5);
            }
        }

        private void EnsureScaleTransform(Image image)
        {
            if (image.RenderTransform == null || !(image.RenderTransform is ScaleTransform))
            {
                image.RenderTransform = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
                image.RenderTransformOrigin = new Point(0.5, 0.5);
            }
        }

        /// <summary>
        /// Resetea todas las transformaciones de la imagen
        /// </summary>
        public void ResetTransform(Image image)
        {
            try
            {
                image.RenderTransform = null;
                image.Opacity = 1.0;
            }
            catch { }
        }

        /// <summary>
        /// Habilita o deshabilita animaciones globalmente
        /// </summary>
        public void SetAnimationsEnabled(bool enabled)
        {
            _animationsEnabled = enabled;
            ModernLogger.Info($"🎬 Animaciones: {(enabled ? "HABILITADAS" : "DESHABILITADAS")}");
        }
    }
}
