using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ComicReader.Converters
{
    public class SelectedSectionToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var selected = value as string;
                var param = parameter as string;
                if (string.IsNullOrEmpty(param)) return Visibility.Collapsed;
                return string.Equals(selected, param, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { return Visibility.Collapsed; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
