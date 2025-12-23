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
    private ILocalSettingsService LocalSettingsService { get; }

    [ObservableProperty]
    [SavedSetting("VRC_UseNativeTracking", false)]
    private bool _useNativeVrcEyeTracking;

    [ObservableProperty]
    private string? _selectedModuleMode = "Face";

    [ObservableProperty] private bool _vrcftDetected;

    public ObservableCollection<string> ModuleModeOptions { get; set; } = ["Both", "Face", "Eyes", "Disabled"];

    private string _baballoniaModulePath;

    private bool TryGetModuleConfig(out ModuleConfig? config)
    {
        if (!Directory.Exists(Utils.VrcftLibsDirectory))
        {
            config = null;
            return false;
        }

        var moduleFiles = Directory.GetFiles(Utils.VrcftLibsDirectory, "*.json", SearchOption.AllDirectories);
        foreach (var moduleFile in moduleFiles)
        {
            if (Path.GetFileName(moduleFile) != "BabbleConfig.json") continue;

            var contents = File.ReadAllText(moduleFile);
            var possibleBabbleConfig = JsonSerializer.Deserialize<ModuleConfig>(contents);
            if (possibleBabbleConfig != null) _baballoniaModulePath = moduleFile;
            config = possibleBabbleConfig;
            return true;
        }
        config = null;
        return false;
    }

    public VrcViewModel()
    {
        LocalSettingsService = Ioc.Default.GetRequiredService<ILocalSettingsService>();

        _vrcftDetected = TryGetModuleConfig(out var config);
        if (_vrcftDetected && config is not null)
        {
            _selectedModuleMode = config.IsEyeSupported switch
            {
                true => config.IsFaceSupported ? "Both" : "Eyes",
                false => config.IsFaceSupported ? "Face" : "Disabled"
            };
        }

        _ = LoadAsync();
        PropertyChanged += (_, _) => { LocalSettingsService.Save(this); };
    }

    private async Task LoadAsync()
    {
        var useNative = LocalSettingsService.ReadSetting("VRC_UseNativeTracking", false);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            UseNativeVrcEyeTracking = useNative;
        }, DispatcherPriority.Background);
    }

    private async Task WriteModuleConfig(ModuleConfig config)
    {
        if (!string.IsNullOrWhiteSpace(_baballoniaModulePath))
            await File.WriteAllTextAsync(_baballoniaModulePath, JsonSerializer.Serialize(config));
    }

    async partial void OnSelectedModuleModeChanged(string? value)
    {
        try
        {
            if (!TryGetModuleConfig(out var oldConfig)) return;

            var newConfig = value switch
            {
                "Both" => new ModuleConfig(oldConfig!.Host, oldConfig.Port, true, true),
                "Eyes" => new ModuleConfig(oldConfig!.Host, oldConfig.Port, true, false),
                "Face" => new ModuleConfig(oldConfig!.Host, oldConfig.Port, false, true),
                "Disabled" => new ModuleConfig(oldConfig!.Host, oldConfig.Port, false, false),
                _ => throw new InvalidOperationException()
            };
            await WriteModuleConfig(newConfig);
        }
        catch (Exception)
        {
            // ignore lol
        }
    }
}
