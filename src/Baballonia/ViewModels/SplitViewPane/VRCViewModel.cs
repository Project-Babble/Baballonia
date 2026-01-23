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
    [ObservableProperty]
    [property: SavedSetting("VRC_UseNativeTracking", false)]
    private bool _useNativeVrcEyeTracking;

    [ObservableProperty]
    private string? _selectedModuleMode = "Face";

    [ObservableProperty]
    private bool _vrcftDetected;

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
            if (string.IsNullOrEmpty(contents))
            {
                // How do we even get here??
                config = null;
                return false;
            }
            var possibleBabbleConfig = JsonSerializer.Deserialize<ModuleConfig>(contents);
            if (possibleBabbleConfig != null) _baballoniaModulePath = moduleFile;
            config = possibleBabbleConfig;
            return true;
        }
        config = null;
        return false;
    }

    public VrcViewModel(ILocalSettingsService localSettingsService)
    {
        VrcftDetected = TryGetModuleConfig(out var config);
        if (VrcftDetected && config is not null)
        {
            SelectedModuleMode = config.IsEyeSupported switch
            {
                true => config.IsFaceSupported ? "Both" : "Eyes",
                false => config.IsFaceSupported ? "Face" : "Disabled"
            };
        }

        PropertyChanged += (_, p) =>
        {
            localSettingsService.Save(this);
        };
        localSettingsService.Load(this);
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
