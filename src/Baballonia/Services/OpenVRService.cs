using Microsoft.Extensions.Logging;
using System;
using System.IO;
using Valve.VR;

namespace Baballonia.Services;

public class OpenVRService(ILogger<OpenVRService> logger)
{
    //app key needed for vrmanifest
    private const string ApplicationKey = "projectbabble.Baballonia";

    // Registers the .vrmanifest so SteamVR knows about the app. Safe to call repeatedly;
    // returns false (never throws) when SteamVR isn't running.
    public bool AutoStart() => WithSession(RegisterManifest);

    // Ensures the manifest is registered, swallowing the unsupported-OS case.
    public void CheckIfReadyIfIsnt()
    {
        try
        {
            AutoStart();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "DLL not found! Your current OS might not be supported for SteamVR AutoStart");
        }
    }

    //bool for checking, getting, and setting the application key for auto launch
    public bool SteamvrAutoStart
    {
        get => WithSession(apps => apps.GetApplicationAutoLaunch(ApplicationKey));
        set
        {
            var enabled = value;
            WithSession(apps =>
            {
                if (!RegisterManifest(apps))
                    return false;

                var result = apps.SetApplicationAutoLaunch(ApplicationKey, enabled);
                if (result != EVRApplicationError.None)
                {
                    logger.LogError("Failed to set SteamVR AutoStart: {0}", result);
                    return false;
                }

                return true;
            });
        }
    }

    // Opens a short-lived background OpenVR session, runs the action, then always shuts down.
    // Re-initialising per call means SteamVR being closed between calls surfaces as an init
    // error rather than crashing or hanging the process on a now-invalid native interface
    // (OpenVR.Shutdown invalidates all cached interface pointers, so a stale session that
    // outlived SteamVR must never be reused).
    private bool WithSession(Func<CVRApplications, bool> action)
    {
        EVRInitError error = EVRInitError.None;
        OpenVR.Init(ref error, EVRApplicationType.VRApplication_Background);
        if (error != EVRInitError.None)
        {
            logger.LogWarning("Unable to toggle autostart; SteamVR issue (Is it even running?): {0}", error);
            return false;
        }

        try
        {
            var applications = OpenVR.Applications;
            if (applications == null)
            {
                logger.LogWarning("SteamVR applications interface is unavailable.");
                return false;
            }

            return action(applications);
        }
        finally
        {
            OpenVR.Shutdown();
        }
    }

    private bool RegisterManifest(CVRApplications applications)
    {
        // Locate the manifest.vrmanifest next to the executable.
        string? fullManifestPath = Path.GetDirectoryName(AppContext.BaseDirectory);
        if (fullManifestPath == null)
        {
            logger.LogWarning("Cannot find the executable path to locate manifest.vrmanifest");
            return false;
        }

        var vrManifestPath = Path.GetFullPath(Path.Combine(fullManifestPath, "manifest.vrmanifest"));
        var registerResult = applications.AddApplicationManifest(vrManifestPath, false);
        if (registerResult != EVRApplicationError.None)
        {
            logger.LogWarning("Failed to register vrmanifest: {0}", registerResult);
            return false;
        }

        logger.LogDebug("Application installed check: {0}", applications.IsApplicationInstalled(ApplicationKey));
        return true;
    }
}
