using System.Collections.Generic;
using Baballonia.Contracts;
using Android.App;
using Android.Content;
using Android.Hardware.Camera2;
using Android.Hardware.Usb;
using Android.Util;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Baballonia.Android;

public sealed class AndroidDeviceEnumerator : IDeviceEnumerator
{
    public ILogger Logger { get; set; }
    public Dictionary<string, string> Cameras { get; set; }

    /// <summary>
    /// Lists available cameras with friendly names as dictionary keys and device identifiers as values.
    /// </summary>
    /// <returns>Dictionary with friendly names as keys and device IDs as values</returns>
    public async Task<Dictionary<string, string>> UpdateCameras()
    {
        var cameraDict = new Dictionary<string, string>();

        try
        {
            var context = Application.Context;
            var cameraManager = (CameraManager)context.GetSystemService(Context.CameraService);
            var usbManager = (UsbManager)context.GetSystemService(Context.UsbService);

            // Enumerate built-in cameras using Camera2 API
            var cameraIds = cameraManager.GetCameraIdList();
            foreach (var cameraId in cameraIds)
            {
                try
                {
                    var characteristics = cameraManager.GetCameraCharacteristics(cameraId);

                    // Get camera facing direction
                    var facing = characteristics.Get(CameraCharacteristics.LensFacing);
                    string friendlyName;

                    if (facing != null)
                    {
                        switch ((int)facing)
                        {
                            case (int)LensFacing.Front:
                                friendlyName = $"Front Camera ({cameraId})";
                                break;
                            case (int)LensFacing.Back:
                                friendlyName = $"Back Camera ({cameraId})";
                                break;
                            case (int)LensFacing.External:
                                friendlyName = $"External Camera ({cameraId})";
                                break;
                            default:
                                friendlyName = $"Camera {cameraId}";
                                break;
                        }
                    }
                    else
                    {
                        friendlyName = $"Camera {cameraId}";
                    }

                    // For built-in cameras, use the numeric camera ID as the device identifier
                    EnsureUniqueKey(cameraDict, friendlyName, cameraId);
                }
                catch (System.Exception ex)
                {
                    Log.Error("AndroidDeviceEnumerator", $"Error getting camera {cameraId}: {ex.Message}");
                    // Still add the camera with a generic name
                    EnsureUniqueKey(cameraDict, $"Camera {cameraId}", cameraId);
                }
            }

            // Enumerate USB cameras (UVC devices)
            var deviceList = usbManager.DeviceList;
            foreach (var device in deviceList.Values)
            {
                if (IsUVCDevice(device))
                {
                    string deviceName = device.DeviceName ?? "Unknown USB Camera";
                    string friendlyName = deviceName;

                    // For USB cameras, prefix the device identifier with "USB:" to distinguish from built-in cameras
                    string deviceId = $"USB:{deviceName}";
                    EnsureUniqueKey(cameraDict, friendlyName, deviceId);
                }
            }
        }
        catch (System.Exception ex)
        {
            Log.Error("AndroidDeviceEnumerator", $"Error enumerating cameras: {ex.Message}");
            cameraDict.Add($"Error: {ex.Message}", "error");
        }

        return cameraDict;
    }

    private bool IsUVCDevice(UsbDevice device)
    {
        // Check if device has Video class interface (class 14)
        for (int i = 0; i < device.InterfaceCount; i++)
        {
            var usbInterface = device.GetInterface(i);
            if (usbInterface.InterfaceClass == UsbClass.Video)
            {
                return true;
            }
        }
        return false;
    }

    private void EnsureUniqueKey(Dictionary<string, string> cameraDict, string friendlyName, string deviceId)
    {
        string key = friendlyName;
        int counter = 1;

        while (cameraDict.ContainsKey(key))
        {
            key = $"{friendlyName} ({counter})";
            counter++;
        }

        cameraDict[key] = deviceId;
    }
}
