using System;
using System.Globalization;
using System.Windows.Data;

namespace ComicReader.Converters
{
    /// <summary>
    /// Inverts a boolean value. Used so a CheckBox can present an inverted
    /// semantic of an underlying setting without renaming the property.
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return false;
        }
    }
}
