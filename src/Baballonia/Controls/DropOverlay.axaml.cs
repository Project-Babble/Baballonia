using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Baballonia.Contracts;
using Baballonia.ViewModels.SplitViewPane;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Baballonia.Controls;

public partial class DropOverlay : UserControl
{
    private readonly ILocalSettingsService _localSettingsService;

    public DropOverlay()
    {
        _localSettingsService = Ioc.Default.GetRequiredService<ILocalSettingsService>();
        InitializeComponent(true);
        IsOverlayVisible = true;
    }

    public static readonly StyledProperty<bool> IsOverlayVisibleProperty =
        AvaloniaProperty.Register<DropOverlay, bool>(nameof(IsOverlayVisible), false);

    public bool IsOverlayVisible
    {
        get => GetValue(IsOverlayVisibleProperty);
        set => SetValue(IsOverlayVisibleProperty, value);
    }

    private void Changed(object? sender, RoutedEventArgs e)
    {
        _localSettingsService.SaveSetting("SecondsWarningRead", WarningCheckbox.IsChecked);
    }

    private void SecondWarningUnderstood(object? sender, RoutedEventArgs e)
    {
        IsOverlayVisible = false;
    }
}
