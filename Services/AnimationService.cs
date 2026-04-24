using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace ComicReader.Services
{
    /// <summary>
    /// Servicio centralizado para controlar las animaciones en toda la aplicación.
    /// Permite activar/desactivar animaciones globalmente según la preferencia del usuario.
    /// </summary>
    public static class AnimationService
    {
        private static bool? _animationsEnabled = null;

        /// <summary>
        /// Obtiene si las animaciones están habilitadas según la configuración del usuario.
        /// Por defecto es true (habilitadas).
        /// </summary>
        public static bool AreAnimationsEnabled
        {
            get
            {
                if (_animationsEnabled == null)
                {
                    try
                    {
                        _animationsEnabled = SettingsManager.Settings?.AnimationsEnabled != false;
                    }
                    catch
                    {
                        _animationsEnabled = true; // Fallback si falla la lectura
                    }
                }
                return _animationsEnabled.Value;
            }
        }

        /// <summary>
        /// Refresca el estado de animaciones desde la configuración.
        /// Llamar este método después de guardar cambios en Settings.
        /// </summary>
        public static void RefreshAnimationState()
        {
            _animationsEnabled = null; // Fuerza recargar en el siguiente acceso
        }

        /// <summary>
        /// Inicia una animación SOLO si las animaciones están habilitadas.
        /// Si están deshabilitadas, aplica el valor final instantáneamente.
        /// </summary>
        public static void BeginAnimation(System.Windows.Media.Animation.Animatable target, DependencyProperty property, DoubleAnimation animation)
        {
            if (target == null || property == null) return;

            if (AreAnimationsEnabled && animation != null)
            {
                // Animación normal
                target.BeginAnimation(property, animation);
            }
            else if (animation != null)
            {
                // Sin animación: aplicar valor final instantáneamente
                var finalValue = animation.To ?? target.GetValue(property);
                target.SetValue(property, finalValue);
            }
        }

        /// <summary>
        /// Inicia un Storyboard SOLO si las animaciones están habilitadas.
        /// Si están deshabilitadas, aplica los valores finales instantáneamente.
        /// </summary>
        public static void BeginStoryboard(Storyboard storyboard)
        {
            if (storyboard == null) return;

            if (AreAnimationsEnabled)
            {
                // Animación normal
                storyboard.Begin();
            }
            else
            {
                // Sin animación: completar instantáneamente
                storyboard.SkipToFill();
            }
        }

        /// <summary>
        /// Crea una animación de desvanecimiento (Fade) condicional.
        /// Si las animaciones están deshabilitadas, devuelve null.
        /// </summary>
        public static DoubleAnimation CreateFadeAnimation(double from, double to, int durationMs, TimeSpan? beginTime = null)
        {
            if (!AreAnimationsEnabled)
                return null;

            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            if (beginTime.HasValue)
                animation.BeginTime = beginTime.Value;

            return animation;
        }

        /// <summary>
        /// Crea una animación de deslizamiento (Slide) condicional.
        /// Si las animaciones están deshabilitadas, devuelve null.
        /// </summary>
        public static DoubleAnimation CreateSlideAnimation(double from, double to, int durationMs)
        {
            if (!AreAnimationsEnabled)
                return null;

            return new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
        }

        /// <summary>
        /// Aplica un efecto de desvanecimiento a un elemento.
        /// Si las animaciones están deshabilitadas, cambia la opacidad instantáneamente.
        /// </summary>
        public static void FadeElement(UIElement element, double targetOpacity, int durationMs = 300)
        {
            if (element == null) return;

            if (AreAnimationsEnabled)
            {
                var fade = new DoubleAnimation
                {
                    To = targetOpacity,
                    Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                };
                element.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            else
            {
                // Sin animación: cambio instantáneo
                element.Opacity = targetOpacity;
            }
        }
    }
}
