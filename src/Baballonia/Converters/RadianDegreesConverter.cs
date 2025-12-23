using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Baballonia.Converters;

public class RadianDegreesConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            double d => (d * (180f / Math.PI)).ToString(),
            float f => (f * (180f / Math.PI)).ToString(),
            decimal dd => ((float)dd * (180 / Math.PI)).ToString(),
            _ => 0f
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            double d => (d * (Math.PI / 180f)).ToString(),
            float f => (f * (Math.PI / 180f)).ToString(),
            decimal dd => ((float)dd * (Math.PI / 180f)).ToString(),
            _ => 0f
        };
    }
}
