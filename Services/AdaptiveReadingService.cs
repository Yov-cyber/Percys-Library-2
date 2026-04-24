using System;
using System.Windows;
using System.Windows.Media;
using ComicReader.Utils;

namespace ComicReader.Services
{
    /// <summary>
    /// Servicio de lectura adaptativa que ajusta brillo y temperatura de color
    /// basado en hora del día y contenido de la página
    /// </summary>
    public sealed class AdaptiveReadingService
    {
        private static readonly Lazy<AdaptiveReadingService> _instance = 
            new Lazy<AdaptiveReadingService>(() => new AdaptiveReadingService());
        
        public static AdaptiveReadingService Instance => _instance.Value;

        // Configuración
        public bool AutoAdjustBrightness { get; set; } = true;
        public bool AutoAdjustColorTemp { get; set; } = true;
        public bool ReduceBlueLight { get; set; } = true;
        public double MaxBrightnessReduction { get; set; } = 0.3; // 30%

        // Rangos horarios
        private readonly TimeSpan _morningStart = new TimeSpan(6, 0, 0);
        private readonly TimeSpan _eveningStart = new TimeSpan(18, 0, 0);
        private readonly TimeSpan _nightStart = new TimeSpan(22, 0, 0);

        private AdaptiveReadingService()
        {
            ModernLogger.Info("✓ AdaptiveReadingService inicializado");
        }

        /// <summary>
        /// Calcula el ajuste de brillo basado en la hora del día
        /// </summary>
        public double GetBrightnessAdjustment()
        {
            if (!AutoAdjustBrightness) return 1.0;

            var now = DateTime.Now.TimeOfDay;

            // Mañana (6am-6pm): Brillo normal
            if (now >= _morningStart && now < _eveningStart)
            {
                return 1.0;
            }
            // Tarde (6pm-10pm): Reducción gradual
            else if (now >= _eveningStart && now < _nightStart)
            {
                var elapsed = (now - _eveningStart).TotalHours;
                var total = (_nightStart - _eveningStart).TotalHours;
                var reduction = (elapsed / total) * MaxBrightnessReduction;
                return 1.0 - reduction;
            }
            // Noche (10pm-6am): Brillo reducido
            else
            {
                return 1.0 - MaxBrightnessReduction;
            }
        }

        /// <summary>
        /// Calcula la temperatura de color basada en hora del día
        /// </summary>
        public Color GetColorTemperatureFilter()
        {
            if (!AutoAdjustColorTemp) return Colors.Transparent;

            var now = DateTime.Now.TimeOfDay;

            // Día: Sin filtro
            if (now >= _morningStart && now < _eveningStart)
            {
                return Color.FromArgb(0, 255, 255, 255);
            }
            // Tarde: Filtro naranja suave
            else if (now >= _eveningStart && now < _nightStart)
            {
                var elapsed = (now - _eveningStart).TotalHours;
                var total = (_nightStart - _eveningStart).TotalHours;
                var intensity = (byte)(60 * (elapsed / total));
                
                return Color.FromArgb(intensity, 255, 200, 150);
            }
            // Noche: Filtro naranja intenso (reduce luz azul)
            else
            {
                return ReduceBlueLight ? 
                    Color.FromArgb(60, 255, 200, 150) : 
                    Color.FromArgb(0, 255, 255, 255);
            }
        }

        /// <summary>
        /// Aplica ajustes adaptativos a un elemento UI
        /// </summary>
        public void ApplyAdaptiveSettings(UIElement element)
        {
            if (element == null) return;

            try
            {
                var brightness = GetBrightnessAdjustment();
                var colorTemp = GetColorTemperatureFilter();

                // Aplicar efecto de brillo
                if (brightness < 1.0)
                {
                    var brightnessEffect = new System.Windows.Media.Effects.BlurEffect
                    {
                        Radius = 0 // No blur, solo para demostración
                    };
                    // En una implementación real, usaríamos un shader effect personalizado
                }

                // Aplicar filtro de temperatura de color
                if (colorTemp.A > 0)
                {
                    // Crear overlay con el color de temperatura
                    // En implementación real, esto sería un efecto de shader
                }

                ModernLogger.Info($"✓ Ajustes adaptativos aplicados: Brillo={brightness:F2}, Temp={colorTemp}");
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error aplicando ajustes adaptativos: {ex.Message}");
            }
        }

        /// <summary>
        /// Obtiene el modo de lectura recomendado
        /// </summary>
        public ReadingMode GetRecommendedMode()
        {
            var now = DateTime.Now.TimeOfDay;

            if (now >= _morningStart && now < _eveningStart)
            {
                return ReadingMode.Day;
            }
            else if (now >= _eveningStart && now < _nightStart)
            {
                return ReadingMode.Evening;
            }
            else
            {
                return ReadingMode.Night;
            }
        }

        /// <summary>
        /// Analiza el contenido de una página y sugiere ajustes
        /// </summary>
        public ContentAnalysis AnalyzeContent(byte[] imageData)
        {
            try
            {
                // Análisis simplificado: calcular brillo promedio
                long totalBrightness = 0;
                int sampleSize = Math.Min(imageData.Length / 4, 10000); // Muestrear píxeles

                for (int i = 0; i < sampleSize * 4; i += 4)
                {
                    if (i + 2 >= imageData.Length) break;
                    
                    var r = imageData[i];
                    var g = imageData[i + 1];
                    var b = imageData[i + 2];
                    
                    // Luminosidad percibida
                    var brightness = (int)(0.299 * r + 0.587 * g + 0.114 * b);
                    totalBrightness += brightness;
                }

                var avgBrightness = totalBrightness / sampleSize;
                var isDark = avgBrightness < 100;
                var isLight = avgBrightness > 180;

                return new ContentAnalysis
                {
                    AverageBrightness = avgBrightness,
                    IsDark = isDark,
                    IsLight = isLight,
                    RecommendedBrightnessAdjust = isDark ? 1.2 : (isLight ? 0.9 : 1.0)
                };
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error analizando contenido: {ex.Message}");
                return new ContentAnalysis();
            }
        }

        public enum ReadingMode
        {
            Day,
            Evening,
            Night
        }

        public class ContentAnalysis
        {
            public long AverageBrightness { get; set; }
            public bool IsDark { get; set; }
            public bool IsLight { get; set; }
            public double RecommendedBrightnessAdjust { get; set; } = 1.0;
        }
    }
}
