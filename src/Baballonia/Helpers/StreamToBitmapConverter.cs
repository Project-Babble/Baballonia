using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace AvaloniaMiaDev.Helpers;

public class StreamToBitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var bitmapImages = new List<Bitmap>();
        if (value == null)
            return bitmapImages;

        var imageSources = (List<Stream>)value;
        foreach (var imageSource in imageSources)
        {
            imageSource.Seek(0, SeekOrigin.Begin);
            var bitmapImage = new Bitmap(imageSource);
            bitmapImages.Add(bitmapImage);
        }
        return bitmapImages;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
