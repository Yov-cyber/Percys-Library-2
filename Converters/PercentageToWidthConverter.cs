using System;
using System.Globalization;
using System.Windows.Data;

namespace ComicReader.Converters
{
    // Convierte un porcentaje (0-100) a un ancho en píxeles con parámetro opcional (max width)
    public class PercentageToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                double pct = System.Convert.ToDouble(value);
                double max = 120.0;
                if (parameter != null && double.TryParse(parameter.ToString(), out var p)) max = p;
                pct = Math.Max(0.0, Math.Min(100.0, pct));
                return max * (pct / 100.0);
            }
            catch { return 0.0; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value == null) return Binding.DoNothing;
                double width = System.Convert.ToDouble(value);
                double max = 120.0;
                if (parameter != null && double.TryParse(parameter.ToString(), out var p)) max = p;
                if (max <= 0) return 0.0;
                var pct = (width / max) * 100.0;
                return Math.Max(0.0, Math.Min(100.0, pct));
            }
            catch
            {
                return Binding.DoNothing;
            }
        }
    }
}
