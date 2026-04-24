// FileName: /Converters/ReadingDirectionToFlowDirectionConverter.cs
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ComicReader.Services; // Para ReadingDirection

namespace ComicReader.Converters
{
    public class ReadingDirectionToFlowDirectionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ReadingDirection direction)
            {
                return direction == ReadingDirection.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            }
            return FlowDirection.LeftToRight;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}