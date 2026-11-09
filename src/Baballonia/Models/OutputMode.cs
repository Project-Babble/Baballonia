using Baballonia.Contracts;

namespace Baballonia.Models;

/// <summary>Where tracking data is sent, chosen on the Apps page.</summary>
public enum OutputMode
{
    /// <summary>Only the OSC output on port 8888, for the VRCFaceTracking module and other apps.</summary>
    OtherApps,

    /// <summary>VRChat avatar parameters via the bundled VRCFaceTracking (<see cref="INativeVrcftService"/>).</summary>
    BundledVrcft,

    /// <summary>VRChat's built-in eye and face tracking OSC endpoints.</summary>
    VrchatNative
}

public static class OutputModeSettings
{
    public const string ModeKey = "Apps_OutputMode";
    public const string UseNativeVrcftKey = "AppSettings_UseNativeVRCFT";
    public const string NativeEyeTrackingKey = "VRC_UseNativeTracking";
    public const string NativeFaceTrackingKey = "VRC_UseNativeFaceTracking";

    public static OutputMode Read(ILocalSettingsService settings)
    {
        if (settings.ReadSetting<OutputMode?>(ModeKey) is { } mode)
            return mode;

        // Settings from before the Apps page only had the individual toggles
        if (settings.ReadSetting<bool>(UseNativeVrcftKey))
            return OutputMode.BundledVrcft;
        return settings.ReadSetting<bool>(NativeEyeTrackingKey) ? OutputMode.VrchatNative : OutputMode.OtherApps;
    }

    public static void Save(ILocalSettingsService settings, OutputMode mode)
    {
        settings.SaveSetting(ModeKey, mode);
        settings.SaveSetting(UseNativeVrcftKey, mode == OutputMode.BundledVrcft);
    }
}
