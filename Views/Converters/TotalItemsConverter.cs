using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using ComicReader.Core.Abstractions;

namespace ComicReader.Views.Converters
{
    public class TotalItemsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is System.Collections.IEnumerable list)
            {
                try
                {
                    int total = 0;
                    foreach (var o in list)
                    {
                        if (o is CollectionDto c && c.Items != null) total += c.Items.Count;
                    }
                    return total.ToString();
                }
                catch { }
            }
            return "0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
