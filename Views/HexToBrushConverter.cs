using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ComicReader.Views
{
    [ValueConversion(typeof(string), typeof(Brush))]
    public class HexToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return null;
            if (value is Brush b) return b;
            string s = value as string;
            if (string.IsNullOrWhiteSpace(s)) return null;
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(s);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush scb)
            {
                return scb.Color.ToString();
            }
            return null;
        }
    }
}