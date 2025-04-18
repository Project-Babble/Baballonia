using Avalonia;
using Avalonia.Controls;

namespace AvaloniaMiaDev.Controls;

public partial class SettingsToggle : UserControl
{
    public static readonly StyledProperty<string> IconPathProperty =
        AvaloniaProperty.Register<SettingsExpander, string>(nameof(IconPath));

    public string IconPath
    {
        get => GetValue(IconPathProperty);
        set => SetValue(IconPathProperty, value);
    }

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<SettingsExpander, string>(nameof(Title));

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<SettingsExpander, string>(nameof(Description));

    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public static readonly StyledProperty<string> OnContentProperty =
        AvaloniaProperty.Register<SettingsExpander, string>(nameof(OnContent));

    public string OnContent
    {
        get => GetValue(OnContentProperty);
        set => SetValue(OnContentProperty, value);
    }

    public static readonly StyledProperty<string> OffContentProperty =
        AvaloniaProperty.Register<SettingsExpander, string>(nameof(OffContent));

    public string OffContent
    {
        get => GetValue(OffContentProperty);
        set => SetValue(OffContentProperty, value);
    }

    public static readonly StyledProperty<bool> SettingToggledProperty =
            AvaloniaProperty.Register<SettingsExpander, bool>(nameof(SettingToggled));

    public object SettingToggled
    {
        get => GetValue(SettingToggledProperty);
        set => SetValue(SettingToggledProperty, value);
    }

    public SettingsToggle()
    {
        InitializeComponent();
        OffContent = "Off";
        OnContent = "On";
    }
}
