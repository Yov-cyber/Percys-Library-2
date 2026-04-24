using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ComicReader.Converters
{
    public class SelectedToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool sel = value as bool? == true;
            return sel ? new SolidColorBrush(Color.FromRgb(217,91,42)) : new SolidColorBrush(Color.FromRgb(255,250,240));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
