using Microsoft.Extensions.Logging;
using System;
using System.IO;
using Valve.VR;

namespace Baballonia.Services;

public class OpenVRService(ILogger<OpenVRService> logger)
{
    //app key needed for vrmanifest
    private const string ApplicationKey = "projectbabble.Baballonia";
    private bool _isAutoStartReady;

    //AutoStart function
    public bool AutoStart()
    {
        // Checking if SteamVR is open in the background
        EVRInitError error = EVRInitError.None;
        OpenVR.Init(ref error, EVRApplicationType.VRApplication_Background);

        if (error != EVRInitError.None)
        {
            logger.LogWarning("Failed to Enable SteamVR AutoStart: {0}", error);
            _isAutoStartReady = false;
            return _isAutoStartReady;
        }

        // Trying to check for and find the manifest.vrmanifest file using the exe's directory
        string? fullManifestPath = Path.GetDirectoryName(AppContext.BaseDirectory);
        if (fullManifestPath == null)
        {
            throw new Exception("Can not find the executable Path");
        }
        var VRManifestPath = Path.GetFullPath(Path.Combine(fullManifestPath, "manifest.vrmanifest"));

        // Checking if the manifest is registered and if anything went wrong
        var VRManifestRegResult = OpenVR.Applications.AddApplicationManifest(VRManifestPath, false);
        if(VRManifestRegResult != EVRApplicationError.None)
        {
            logger.LogWarning("Failed to register vrmanifest: {0}", VRManifestRegResult);
            _isAutoStartReady = false;
            return _isAutoStartReady;
        }
        // Checking if the application in the vrmanifest is valid
        var ApplicationCheck = OpenVR.Applications.IsApplicationInstalled(ApplicationKey);
        logger.LogDebug("checking for application {0}", ApplicationCheck);

        logger.LogInformation("Successfully Added to SteamVR startup apps");

        _isAutoStartReady = true;
        return _isAutoStartReady;
    }

    // Checking to see if autostart is ready
    public void CheckIfReadyIfIsnt()
    {
        if (_isAutoStartReady) return;

        try
        {
            AutoStart();
        }
        catch (Exception e)
        {
            logger.LogWarning("DLL not found! Your current OS might not be supported for SteamVR AutoStart", e);
        }
    }

    //bool for checking, getting, and setting the application key for auto launch
    public bool SteamvrAutoStart
    {
        get => _isAutoStartReady && OpenVR.Applications.GetApplicationAutoLaunch(ApplicationKey);
        set
        {
            if (!_isAutoStartReady && !AutoStart())
            {
                logger.LogError("Failed to change SteamVR AutoStart setting. OpenVR could not be Configured.");
                return;
            }

            var SetAutoStartResult = OpenVR.Applications.SetApplicationAutoLaunch(ApplicationKey, value);
            if (SetAutoStartResult != EVRApplicationError.None)
            {
                logger.LogError("Failed to set SteamVR Auto Start: {0}", SetAutoStartResult);
            }
        }
    }
}
