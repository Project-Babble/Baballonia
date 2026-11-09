using Baballonia.Assets;
using Baballonia.Contracts;
using Baballonia.Helpers;
using Baballonia.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Baballonia.ViewModels.SplitViewPane;

public record OutputModeOption(OutputMode Mode, string Header, string Description);

// The "Apps" page
public partial class VrcViewModel : ViewModelBase
{
    [ObservableProperty]
    [property: SavedSetting(OutputModeSettings.NativeEyeTrackingKey, false)]
    private bool _useNativeVrcEyeTracking;

    [ObservableProperty]
    [property: SavedSetting(OutputModeSettings.NativeFaceTrackingKey, false)]
    private bool _useNativeVrcFaceTracking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOtherAppsSelected), nameof(IsBundledVrcftSelected), nameof(IsVrchatNativeSelected))]
    private OutputModeOption _selectedOutputMode;

    // Mirrors the bundled VRCFaceTracking output mode, saved alongside it by OutputModeSettings
    [ObservableProperty]
    private bool _useNativeVrcft;

    public List<OutputModeOption> OutputModes { get; }

    public bool IsOtherAppsSelected => SelectedOutputMode.Mode == OutputMode.OtherApps;
    public bool IsBundledVrcftSelected => SelectedOutputMode.Mode == OutputMode.BundledVrcft;
    public bool IsVrchatNativeSelected => SelectedOutputMode.Mode == OutputMode.VrchatNative;

    public INativeVrcftService NativeVrcft { get; }

    private readonly ILocalSettingsService _localSettingsService;

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

    public VrcViewModel(ILocalSettingsService localSettingsService, INativeVrcftService nativeVrcft)
    {
        _localSettingsService = localSettingsService;
        NativeVrcft = nativeVrcft;

        OutputModes =
        [
            new(OutputMode.OtherApps, Resources.Apps_Output_OtherApps_Header, Resources.Apps_Output_OtherApps_Description),
            new(OutputMode.BundledVrcft, Resources.Apps_Output_BundledVrcft_Header, Resources.Apps_Output_BundledVrcft_Description),
            new(OutputMode.VrchatNative, Resources.Apps_Output_VrchatNative_Header, Resources.Apps_Output_VrchatNative_Description),
        ];
        if (!nativeVrcft.IsSupported)
            OutputModes.RemoveAll(o => o.Mode == OutputMode.BundledVrcft);

        var mode = OutputModeSettings.Read(localSettingsService);
        SelectedOutputMode = OutputModes.FirstOrDefault(o => o.Mode == mode) ?? OutputModes[0];

        VrcftDetected = TryGetModuleConfig(out var config);
        if (VrcftDetected && config is not null)
        {
            SelectedModuleMode = config.IsEyeSupported switch
            {
                true => config.IsFaceSupported ? Resources.Firmware_Mode_Both : Resources.Firmware_Mode_Eyes,
                false => config.IsFaceSupported ? Resources.Firmware_Mode_Face : Resources.Firmware_Mode_None
            };
        }

        // Load before saving on change: Load sets properties one at a time, and saving mid-load
        // would overwrite the not-yet-loaded ones (e.g. face tracking) with their defaults
        localSettingsService.Load(this);
        PropertyChanged += (_, p) =>
        {
            localSettingsService.Save(this);
        };
    }

    partial void OnSelectedOutputModeChanged(OutputModeOption value)
    {
        OutputModeSettings.Save(_localSettingsService, value.Mode);
        UseNativeVrcft = value.Mode == OutputMode.BundledVrcft;
    }

    partial void OnUseNativeVrcftChanged(bool value)
    {
        if (value && !IsBundledVrcftSelected)
            SelectedOutputMode = OutputModes.First(o => o.Mode == OutputMode.BundledVrcft);
        else if (!value && IsBundledVrcftSelected)
            SelectedOutputMode = OutputModes.First(o => o.Mode == OutputMode.OtherApps);
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
