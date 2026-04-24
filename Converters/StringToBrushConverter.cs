using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ComicReader.Converters
{
    public class StringToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value == null) return Brushes.Transparent;
                var s = value.ToString();
                if (string.IsNullOrWhiteSpace(s)) return Brushes.Transparent;
                var color = (Color)ColorConverter.ConvertFromString(s);
                return new SolidColorBrush(color);
            }
            catch { return Brushes.Transparent; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
