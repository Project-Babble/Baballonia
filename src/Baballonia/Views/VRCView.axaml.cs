using System;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Baballonia.Helpers;
using Baballonia.ViewModels.SplitViewPane;

namespace Baballonia.Views;

public partial class VrcView : UserControl
{
    private static readonly string BaballoniaModulePath;

    private readonly ComboBox _trackingMode;

    static VrcView()
    {
        if (!Directory.Exists(Utils.VrcftLibsDirectory)) return;

        var moduleFiles = Directory.GetFiles(Utils.VrcftLibsDirectory, "*.json", SearchOption.AllDirectories);
        foreach (var moduleFile in moduleFiles)
        {
            var contents = File.ReadAllText(moduleFile);
            var possibleBabbleConfig = JsonSerializer.Deserialize<ModuleConfig>(contents);
            if (possibleBabbleConfig != null)
            {
                BaballoniaModulePath = moduleFile;
            }
        }
    }

    public VrcView()
    {
        InitializeComponent();

        _trackingMode = this.Find<ComboBox>("ModeCombo")!;

        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (DataContext is not VrcViewModel vm) return;
            var mode = await vm.LocalSettingsService.ReadSettingAsync<string>("VRC_SelectedModuleMode");

            int index = mode switch
            {
                "Both" => 0,
                "Eyes" => 1,
                "Face" => 2,
                _ => 2
            };
            _trackingMode.SelectedIndex = index;
        });
    }

    private async void ModeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_trackingMode.SelectedItem is not ComboBoxItem comboBoxItem)
            return;

        if (!Directory.Exists(Utils.VrcftLibsDirectory)) return;
        if (string.IsNullOrEmpty(BaballoniaModulePath)) return;

        if (DataContext is not VrcViewModel vm) return;

        var oldConfig = JsonSerializer.Deserialize<ModuleConfig>(await File.ReadAllTextAsync(BaballoniaModulePath));
        var newConfig = comboBoxItem.Name switch
        {
            "Both" => new ModuleConfig(oldConfig!.Host, oldConfig.Port, true, true),
            "Eyes" => new ModuleConfig(oldConfig!.Host, oldConfig.Port, true, false),
            "Face" => new ModuleConfig(oldConfig!.Host, oldConfig.Port, false, true),
            _ => throw new InvalidOperationException()
        };

        await vm.LocalSettingsService.SaveSettingAsync(
            "VRC_SelectedModuleMode",
            comboBoxItem.Name);
        await File.WriteAllTextAsync(BaballoniaModulePath, JsonSerializer.Serialize(newConfig));
    }
}

