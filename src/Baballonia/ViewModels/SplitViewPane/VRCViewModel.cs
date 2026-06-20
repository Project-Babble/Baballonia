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

    private const string BabbleModuleGuid = "360b014b-b57b-450f-8f12-9904618ff370";

    private const string BabbleConfigFile = "BabbleConfig.json";

    private static readonly string _babbleModulePath = Path.Combine(Utils.VrcftLibsDirectory, BabbleModuleGuid);

    private static readonly string _babbleVrcftConfigPath = Path.Combine(_babbleModulePath, BabbleConfigFile);

    private bool TryGetModuleConfig(out ModuleConfig? config)
    {
        // Can we detect a valid install of VRCFaceTracking and a Babble module?
        if (!Directory.Exists(_babbleModulePath))
        {
            config = null;
            return false;
        }

        /* Up until the release of the 3.1.0 VRCFT module, we used to bundle the module config 
        * (BabbleConfig.json) with VRCFT. However, this would overwrite the existing settings file.
        * Now, we set and control it from here.  
        * 
        * 1) If there is already a settings file present, just use it
        * 2) If there *isn't* a file, then this is a fresh install and we'll copy our own file
        */
        if (!File.Exists(_babbleVrcftConfigPath))
            File.Copy(BabbleConfigFile, _babbleVrcftConfigPath);

        // Now we're garunteed to have a settings file!
        var contents = File.ReadAllText(_babbleVrcftConfigPath);

        // Sanity check if the (existing) config file is empty
        if (string.IsNullOrEmpty(contents))
        {
            config = null;
            return false;
        }

        // Sanity check if the (existing) config file is malformed
        var possibleBabbleConfig = JsonSerializer.Deserialize<ModuleConfig>(contents);
        if (possibleBabbleConfig != null)
        {
            // All good? Send it
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
        if (!string.IsNullOrWhiteSpace(_babbleVrcftConfigPath))
            await File.WriteAllTextAsync(_babbleVrcftConfigPath, JsonSerializer.Serialize(config));
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
