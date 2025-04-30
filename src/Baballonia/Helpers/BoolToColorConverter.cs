using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Baballonia.Helpers
{
    public class BoolToColorConverter : IValueConverter
    {
        public static BoolToColorConverter Instance { get; } = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value is bool isActive)
            {
                return isActive ?
                    SolidColorBrush.Parse("#FF1C58D9") : // Active indicator color (accent)
                    SolidColorBrush.Parse("#80808080");   // Inactive indicator color
            }

            return SolidColorBrush.Parse("#80808080");
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
