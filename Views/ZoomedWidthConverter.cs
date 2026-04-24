using System;
using System.Globalization;
using System.Windows.Data;

namespace ComicReader.Views
{
    // Convierte (containerWidth, zoom) => width para la imagen manteniendo margenes
    public class ZoomedWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (values == null || values.Length < 2) return Binding.DoNothing;
                double containerWidth = System.Convert.ToDouble(values[0]);
                double zoom = System.Convert.ToDouble(values[1]);
                // restar paddings y margen del Border (approx 32)
                double avail = Math.Max(16, containerWidth - 32);
                var w = avail * zoom;
                // evitar tamaños extremos
                return Math.Max(100, Math.Min(w, 10000));
            }
            catch { return Binding.DoNothing; }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
