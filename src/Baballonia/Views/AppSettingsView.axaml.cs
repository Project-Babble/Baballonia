using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Baballonia.Contracts;
using Baballonia.Services;
using Baballonia.ViewModels.SplitViewPane;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Baballonia.Views;

public partial class AppSettingsView : UserControl
{
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ILanguageSelectorService _languageSelectorService;
    private readonly ComboBox _themeComboBox;
    private readonly ComboBox _langComboBox;
    private readonly NumericUpDown _selectedMinFreqCutoffUpDown;
    private readonly NumericUpDown _selectedSpeedCutoffUpDown;

    public AppSettingsView()
    {
        InitializeComponent();

        _themeSelectorService = Ioc.Default.GetService<IThemeSelectorService>()!;
        _themeComboBox = this.Find<ComboBox>("ThemeCombo")!;
        _themeComboBox.SelectionChanged += ThemeComboBox_SelectionChanged;

        _languageSelectorService = Ioc.Default.GetService<ILanguageSelectorService>()!;
        _langComboBox = this.Find<ComboBox>("LangCombo")!;
        _langComboBox.SelectionChanged += LangComboBox_SelectionChanged;

        _selectedMinFreqCutoffUpDown = this.Find<NumericUpDown>("SelectedMinFreqCutoffUpDown")!;
        _selectedSpeedCutoffUpDown = this.Find<NumericUpDown>("SelectedSpeedCutoffUpDown")!;

        UpdateThemes();

        if (_themeSelectorService.Theme is null)
        {
            _themeSelectorService.SetTheme(ThemeVariant.Default);
            return;
        }

        if (string.IsNullOrEmpty(_languageSelectorService.Language))
        {
            _languageSelectorService.SetLanguage(LanguageSelectorService.DefaultLanguage);
            return;
        }

        int index = _themeSelectorService.Theme.ToString() switch
        {
            "DefaultTheme" => 0,
            "Light" => 1,
            "Dark" => 2,
            _ => 0
        };
        _themeComboBox.SelectedIndex = index;

        index = _languageSelectorService.Language switch
        {
            "DefaultLanguage" => 0,
            "en-US" => 1,
            "es-ES" => 2,
            "ja-JP" => 3,
            "pl-PL" => 4,
            "zh-CN" => 5,
            "zh-TW" => 6,
            "de-DE" => 7,
            "fr-FR" => 8,
            "it-IT" => 9,
            "ko-KR" => 10,
            "pt-BR" => 11,
            "pt-PT" => 12,
            "ru-RU" => 13,
            "ar-SA" => 14,
            "tr-TR" => 15,
            "nl-NL" => 16,
            "sv-SE" => 17,
            "fi-FI" => 18,
            "da-DK" => 19,
            "no-NO" => 20,
            "cs-CZ" => 21,
            "hu-HU" => 22,
            "ro-RO" => 23,
            "vi-VN" => 24,
            "uk-UA" => 25,
            "el-GR" => 26,
            "he-IL" => 27,
            "af-ZA" => 28,
            "ca-ES" => 29,
            "sr-SP" => 30,
            _ => 0
        };
        _langComboBox.SelectedIndex = index;
    }

    ~AppSettingsView()
    {
        _themeComboBox.SelectionChanged -= ThemeComboBox_SelectionChanged;
    }

    private void ThemeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_themeComboBox.SelectedItem is not ComboBoxItem comboBoxItem)
            return;

        ThemeVariant variant = ThemeVariant.Default;
        variant = comboBoxItem!.Name switch
        {
            "DefaultTheme" => ThemeVariant.Default,
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => variant
        };
        _themeSelectorService.SetTheme(variant);
    }

    private void LangComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var item = _langComboBox.SelectedItem as ComboBoxItem;
        _languageSelectorService.SetLanguage(item!.Tag!.ToString()!);
    }

    // Workaround for https://github.com/AvaloniaUI/Avalonia/issues/4460
    private void UpdateThemes()
    {
        var selectedIndex = _themeComboBox.SelectedIndex;
        _themeComboBox.Items.Clear();
        _themeComboBox.Items.Add(new ComboBoxItem
            { Content = Assets.Resources.Settings_Theme_Default_Content, Name = "DefaultTheme" });
        _themeComboBox.Items.Add(new ComboBoxItem
            { Content = Assets.Resources.Settings_Theme_Light_Content, Name = "Light" });
        _themeComboBox.Items.Add(new ComboBoxItem
            { Content = Assets.Resources.Settings_Theme_Dark_Content, Name = "Dark" });
        _themeComboBox.SelectedIndex = selectedIndex;
    }

    private void LaunchFirstTimeSetUp(object? sender, RoutedEventArgs e)
    {
        switch (Application.Current?.ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                OnboardingView.ShowOnboarding(desktop.MainWindow!);
                break;
        }
    }

    private void SelectedSpeedCutoffComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox) return;

        _selectedSpeedCutoffUpDown.Value = comboBox.SelectedIndex switch
        {
            0 => 0.5m,
            1 => 1,
            2 => 2,
            _ => _selectedSpeedCutoffUpDown.Value
        };
    }

    private void SelectedMinFreqCutoffComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox) return;

        _selectedMinFreqCutoffUpDown.Value = comboBox.SelectedIndex switch
        {
            0 => 0.5m,
            1 => 1,
            2 => 2,
            _ => _selectedMinFreqCutoffUpDown.Value
        };
    }

    public void RequestMachineId(object? sender, RoutedEventArgs routedEventArgs)
    {
        if (DataContext is not AppSettingsViewModel vm) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await clipboard.SetTextAsync(vm.MachineID);
        });
    }
}
