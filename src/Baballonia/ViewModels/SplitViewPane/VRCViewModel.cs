using Baballonia.Assets;
using Baballonia.Contracts;
using Baballonia.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Baballonia.ViewModels.SplitViewPane;

public partial class VrcViewModel : ViewModelBase
{
    [ObservableProperty]
    [property: SavedSetting("VRC_UseNativeTracking", false)]
    private bool _useNativeVrcEyeTracking;

    [ObservableProperty]
    private string? _selectedModuleMode = Resources.Firmware_Mode_Face;

    [ObservableProperty]
    private bool _vrcftDetected;

    public ObservableCollection<string> ModuleModeOptions { get; } = [
        Resources.Firmware_Mode_Both,
        Resources.Firmware_Mode_Face,
        Resources.Firmware_Mode_Eyes,
        Resources.Firmware_Mode_None
    ];

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
                true => config.IsFaceSupported ? Resources.Firmware_Mode_Both : Resources.Firmware_Mode_Eyes,
                false => config.IsFaceSupported ? Resources.Firmware_Mode_Face : Resources.Firmware_Mode_None
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
                var v when v == Resources.Firmware_Mode_Both => new ModuleConfig(oldConfig!.Host, oldConfig.Port, true, true),
                var v when v == Resources.Firmware_Mode_Eyes => new ModuleConfig(oldConfig!.Host, oldConfig.Port, true, false),
                var v when v == Resources.Firmware_Mode_Face => new ModuleConfig(oldConfig!.Host, oldConfig.Port, false, true),
                var v when v == Resources.Firmware_Mode_None => new ModuleConfig(oldConfig!.Host, oldConfig.Port, false, false),
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
