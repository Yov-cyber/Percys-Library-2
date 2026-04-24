using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ComicReader.Converters
{
    // Devuelve un emoji/texto corto para cada sección del hub de settings.
    public class SectionToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value as string;
            if (string.IsNullOrEmpty(s)) return "◻";
            s = s.ToLowerInvariant();
            if (s.Contains("apariencia") || s.Contains("appearance"))
                return "🎨";
            if (s.Contains("general"))
                return "⚙️";
            if (s.Contains("lectura") || s.Contains("reading"))
                return "📖";
            if (s.Contains("avanzado") || s.Contains("advanced"))
                return "🔧";
            if (s.Contains("favoritos") || s.Contains("favorites"))
                return "★";
            return "•";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
