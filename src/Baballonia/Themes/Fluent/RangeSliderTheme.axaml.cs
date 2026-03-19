using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using System;

namespace Baballonia.Themes.Fluent;

public class RangeSliderTheme : Styles, IResourceNode
{
    public RangeSliderTheme(IServiceProvider? sp = null) =>
        AvaloniaXamlLoader.Load(sp, this);

    bool IResourceNode.TryGetResource(object key, ThemeVariant? theme, out object? value) =>
        TryGetResource(key, theme, out value);
}
