using System;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using Baballonia.Contracts;

namespace Baballonia.Services;

public class ThemeSelectorService(ILocalSettingsService localSettingsService) : IThemeSelectorService
{
    public event Action<ThemeVariant>? ThemeChanged;

    private const string SettingsKey = "AppBackgroundRequestedTheme";

    public ThemeVariant Theme { get; private set; } = ThemeVariant.Default;

    public void Initialize()
    {
        Theme = LoadThemeFromSettingsAsync();
        SetRequestedTheme();
    }

    public void SetTheme(ThemeVariant theme)
    {
        Theme = theme;
        SetRequestedTheme();
        SaveThemeInSettingsAsync(Theme);
        ThemeChanged?.Invoke(Theme);
    }

    public void SetRequestedTheme()
    {
        Dispatcher.UIThread.Invoke(() => Application.Current!.RequestedThemeVariant = Theme);
    }

    private ThemeVariant LoadThemeFromSettingsAsync()
    {
        var themeName = localSettingsService.ReadSetting<string>(SettingsKey, "Default");
        return themeName switch
        {
            "Default" => ThemeVariant.Default,
            "Dark" => ThemeVariant.Dark,
            "Light" => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
    }

    private void SaveThemeInSettingsAsync(ThemeVariant theme)
    {
        localSettingsService.SaveSetting(SettingsKey, theme.ToString());
    }
}
