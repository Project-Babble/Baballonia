using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Styling;

namespace AvaloniaMiaDev.Helpers;

public class EnumToBooleanConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is string enumString)
        {
            if (!Enum.IsDefined(typeof(ThemeVariant), value!))
            {
                throw new ArgumentException("ExceptionEnumToBooleanConverterValueMustBeAnEnum");
            }

            var enumValue = Enum.Parse(typeof(ThemeVariant), enumString);

            return enumValue.Equals(value);
        }

        throw new ArgumentException("ExceptionEnumToBooleanConverterParameterMustBeAnEnumName");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is string enumString)
        {
            return Enum.Parse(typeof(ThemeVariant), enumString);
        }

        throw new ArgumentException("ExceptionEnumToBooleanConverterParameterMustBeAnEnumName");
    }
}
