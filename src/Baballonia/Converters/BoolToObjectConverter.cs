using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Baballonia.Converters
{
    /// <summary>
    /// Converts a boolean value to and from other types using the TrueValue and FalseValue properties.
    /// </summary>
    public class BoolToObjectConverter : IValueConverter
    {
        /// <summary>
        /// Gets or sets the value to return when the boolean is true.
        /// </summary>
        public object TrueValue { get; set; } = true;

        /// <summary>
        /// Gets or sets the value to return when the boolean is false.
        /// </summary>
        public object FalseValue { get; set; } = false;

        /// <summary>
        /// Static instance that returns true when the value is true, false otherwise.
        /// </summary>
        public static readonly BoolToObjectConverter Default = new();

        /// <summary>
        /// Static instance that returns a double value (1.0 for true, 0.0 for false).
        /// </summary>
        public static readonly BoolToObjectConverter TrueToDouble = new() 
        { 
            TrueValue = double.NaN, // Auto size
            FalseValue = 0.0 // Collapsed
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? TrueValue : FalseValue;
            }
            
            // If the value is not a boolean, try to convert it
            if (value != null && bool.TryParse(value.ToString(), out bool result))
            {
                return result ? TrueValue : FalseValue;
            }

            return FalseValue; // Default to FalseValue if conversion fails
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (Equals(value, TrueValue))
                return true;
            if (Equals(value, FalseValue))
                return false;

            // If values are not directly equal, try comparing string representations
            string valueStr = value?.ToString() ?? string.Empty;
            string trueStr = TrueValue?.ToString() ?? string.Empty;
            string falseStr = FalseValue?.ToString() ?? string.Empty;

            if (string.Equals(valueStr, trueStr, StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(valueStr, falseStr, StringComparison.OrdinalIgnoreCase))
                return false;

            // If no match found, return false by default
            return false;
        }
    }
}
