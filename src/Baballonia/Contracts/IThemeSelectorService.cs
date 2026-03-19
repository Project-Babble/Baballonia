using System;
using Avalonia.Styling;

namespace Baballonia.Contracts;

public interface IThemeSelectorService
{
    public event Action<ThemeVariant>? ThemeChanged;

    ThemeVariant Theme
    {
        get;
    }

    void Initialize();

    void SetTheme(ThemeVariant theme);

    void SetRequestedTheme();
}
