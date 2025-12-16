using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Baballonia.Converters;

public class GammaConverter : IValueConverter
{
    private const double MinGamma = 0.5;
    private const double MaxGamma = 2.0;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            double d => InverseLerp(MinGamma, MaxGamma, d),
            float f => InverseLerp(MinGamma, MaxGamma, f),
            decimal dd => InverseLerp(MinGamma, MaxGamma, (float)dd),
            _ => 0.0
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            double d => double.Lerp(MinGamma, MaxGamma, d),
            float f => double.Lerp(MinGamma, MaxGamma, f),
            decimal dd => double.Lerp(MinGamma, MaxGamma, (float)dd),
            _ => 0.0
        };
    }

    private double InverseLerp(double a, double b, double v)
    {
        return (v - a) / (b - a);
    }
}
