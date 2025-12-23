using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Baballonia.Contracts;
using Baballonia.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Baballonia.ViewModels.SplitViewPane;

public partial class VrcViewModel : ViewModelBase
{
    public ILocalSettingsService LocalSettingsService { get; }

    [ObservableProperty] [property: SavedSetting("VRC_UseNativeTracking", false)]
    private bool _useNativeVrcEyeTracking;

    [ObservableProperty] [property: SavedSetting("VRC_SelectedModuleMode", "Face")]
    private string? _selectedModuleMode = "Face";

    public ObservableCollection<string> ModuleModeOptions { get; set; } = ["Both", "Face", "Eyes"];

    private static readonly string _baballoniaModulePath;

    static VrcViewModel()
    {
        if (!Directory.Exists(Utils.VrcftLibsDirectory)) return;

        var moduleFiles = Directory.GetFiles(Utils.VrcftLibsDirectory, "*.json", SearchOption.AllDirectories);
        foreach (var moduleFile in moduleFiles)
        {
            if (Path.GetFileName(moduleFile) != "BabbleConfig.json") continue;

            var contents = File.ReadAllText(moduleFile);
            var possibleBabbleConfig = JsonSerializer.Deserialize<ModuleConfig>(contents);
            if (possibleBabbleConfig != null)
            {
                _baballoniaModulePath = moduleFile;
            }
        }
    }

    public VrcViewModel()
    {
        LocalSettingsService = Ioc.Default.GetRequiredService<ILocalSettingsService>();

        _ = LoadAsync();
        PropertyChanged += (_, _) => { LocalSettingsService.Save(this); };
    }

    private async Task LoadAsync()
    {
        var selected = LocalSettingsService.ReadSetting<string>("VRC_SelectedModuleMode", "Face");
        var useNative = LocalSettingsService.ReadSetting<bool>("VRC_UseNativeTracking", false);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            SelectedModuleMode = selected;
            UseNativeVrcEyeTracking = useNative;
        }, DispatcherPriority.Background);
    }

    async partial void OnSelectedModuleModeChanged(string? value)
    {
        try
        {
            if (!Directory.Exists(Utils.VrcftLibsDirectory)) return;
            if (string.IsNullOrEmpty(_baballoniaModulePath)) return;

            var oldConfig = JsonSerializer.Deserialize<ModuleConfig>(await File.ReadAllTextAsync(_baballoniaModulePath));
            var newConfig = value switch
            {
                "Both" => new ModuleConfig(oldConfig!.Host, oldConfig.Port, true, true),
                "Eyes" => new ModuleConfig(oldConfig!.Host, oldConfig.Port, true, false),
                "Face" => new ModuleConfig(oldConfig!.Host, oldConfig.Port, false, true),
                _ => throw new InvalidOperationException()
            };

            await File.WriteAllTextAsync(_baballoniaModulePath, JsonSerializer.Serialize(newConfig));
        }
        catch (Exception e)
        {
            // ignore lol
        }
    }
}
