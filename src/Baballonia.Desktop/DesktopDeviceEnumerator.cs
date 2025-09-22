using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

#if WINDOWS
using DirectShowLib;
#endif

namespace Baballonia.Desktop;

public sealed class DesktopDeviceEnumerator(ILogger<DesktopDeviceEnumerator> logger) : IDeviceEnumerator
{
    public ILogger Logger { get; set; } = logger;
    public Dictionary<string, string> Cameras { get; set; } = null!;

    /// <summary>
    /// Lists available cameras with friendly names as dictionary keys and device identifiers as values.
    /// </summary>
    /// <returns>Dictionary with friendly names as keys and device IDs as values</returns>
    public Dictionary<string, string> UpdateCameras()
    {
        Logger.LogDebug("Starting camera device enumeration...");
        Dictionary<string, string> cameraDict = new Dictionary<string, string>();

        try
        {
            Logger.LogDebug("Detecting operating system for camera enumeration");
            if (OperatingSystem.IsWindows())
            {
                Logger.LogDebug("Running on Windows - using DirectShow camera detection");
                AddWindowsDsCameras(cameraDict);
            }
            else if (OperatingSystem.IsMacOS())
            {
                Logger.LogDebug("Running on macOS - using OpenCV camera detection");
                AddOpenCvCameras(cameraDict);
            }
            else if (OperatingSystem.IsLinux())
            {
                Logger.LogDebug("Running on Linux - using UVC device");
                AddLinuxUvcDevices(cameraDict);
            }
            else
            {
                Logger.LogWarning("Unknown operating system detected for camera enumeration");
            }

            // Add serial ports as potential camera sources
            Logger.LogDebug("Adding serial ports as potential camera sources");
            AddSerialPorts(cameraDict);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during camera enumeration: {ErrorMessage}", ex.Message);
            cameraDict.Add($"Error: {ex.Message}", "error");
        }

        Logger.LogDebug("Camera enumeration completed. Found {CameraCount} devices", cameraDict.Count);
        foreach (var camera in cameraDict)
        {
            Logger.LogDebug("Detected camera: '{FriendlyName}' -> '{DeviceId}'", camera.Key, camera.Value);
        }

        return cameraDict;
    }

    private void AddOpenCvCameras(Dictionary<string, string> cameraDict)
    {
        int index = 0;

        while (true)
        {
            var capture = new VideoCapture(index);
            if (!capture.IsOpened())
            {
                break;
            }

            var deviceId = index.ToString();
            string friendlyName = $"Camera {deviceId}";

            // Make sure we don't add duplicate keys
            EnsureUniqueKey(cameraDict, friendlyName, deviceId);

            // Also add the /dev/video path for Linux systems
            if (OperatingSystem.IsLinux())
            {
                string devPath = $"/dev/video{index}";
                EnsureUniqueKey(cameraDict, $"Video Device {index}", devPath);
            }

            capture.Release();
            index++;
        }
    }

    [SupportedOSPlatform("windows")]
    private void AddWindowsDsCameras(Dictionary<string, string> cameraDict)
    {
        #if WINDOWS
        var videoInputDevices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);

        for (var index = 0; index < videoInputDevices.Length; index++)
        {
            var dev = videoInputDevices[index];
            logger.LogDebug("Found device: {}, ClassId: {}, Path: {}", dev.Name, dev.ClassID, dev.DevicePath);
            EnsureUniqueKey(cameraDict, dev.Name, index.ToString());
        }
        #endif
    }

    [SupportedOSPlatform("linux")]
    private void AddLinuxUvcDevices(Dictionary<string, string> cameraDict)
    {
        [DllImport("libudev.so")] static extern IntPtr udev_new();
        [DllImport("libudev.so")] static extern IntPtr udev_unref(IntPtr udev);
        [DllImport("libudev.so")] static extern IntPtr udev_enumerate_new(IntPtr udev);
        [DllImport("libudev.so")] static extern int udev_enumerate_add_match_subsystem(IntPtr udevEnumerate, [MarshalAs(UnmanagedType.LPUTF8Str)] string subsystem);
        [DllImport("libudev.so")] static extern int udev_enumerate_scan_devices(IntPtr udevEnumerate);
        [DllImport("libudev.so")] static extern IntPtr udev_enumerate_get_list_entry(IntPtr udevEnumerate);
        [DllImport("libudev.so")] static extern IntPtr udev_list_entry_get_next(IntPtr listEntry);
        [DllImport("libudev.so")] static extern IntPtr udev_list_entry_get_name(IntPtr listEntry);
        [DllImport("libudev.so")] static extern IntPtr udev_device_new_from_syspath(IntPtr udev, IntPtr syspath);
        [DllImport("libudev.so")] static extern IntPtr udev_device_get_devnode(IntPtr udevDevice);
        [DllImport("libudev.so")] static extern IntPtr udev_device_get_property_value(IntPtr udevDevice, [MarshalAs(UnmanagedType.LPUTF8Str)] string property);
        [DllImport("libudev.so")] static extern IntPtr udev_enumerate_unref(IntPtr udevEnumerate);

        [DllImport("libc.so.6")] static extern int open(IntPtr file, int oflag, int unused);
        [DllImport("libc.so.6")] static extern int close(int fd);
        [DllImport("libc.so.6", SetLastError = true)] static extern int ioctl(int fd, nuint request, ref uint arg);

        const int oRdwr = 0x2, oNonblock = 0x800, eintr = 4, eagain = 11, etimedout = 110;
        const uint vidiocQuerycap = 0x80685600, v4L2CapVideoCapture = 0x1, v4L2CapDeviceCaps = 0x80000000;

        try
        {
            IntPtr udev = udev_new();
            IntPtr enumerate = udev_enumerate_new(udev);
            try
            {
                udev_enumerate_add_match_subsystem(enumerate, "video4linux");
                udev_enumerate_scan_devices(enumerate);
                Span<uint> capsStruct = stackalloc uint[26];
                for (IntPtr iter = udev_enumerate_get_list_entry(enumerate); iter != IntPtr.Zero; iter = udev_list_entry_get_next(iter))
                {
                    IntPtr syspath = udev_list_entry_get_name(iter);
                    IntPtr udevDevice = udev_device_new_from_syspath(udev, syspath);
                    IntPtr v4L2Device = udev_device_get_devnode(udevDevice);

                    int fd = open(v4L2Device, oRdwr | oNonblock, 0);
                    if (fd < 0)
                        continue;

                    try
                    {
                        int result, tries = 0;
                        do
                        {
                            result = ioctl(fd, vidiocQuerycap, ref MemoryMarshal.GetReference(capsStruct));
                        } while (result != 0 && (Marshal.GetLastPInvokeError() is eintr or eagain or etimedout) && ++tries < 4);

                        if (result < 0)
                            continue;
                    }
                    finally
                    {
                        close(fd);
                    }

                    uint caps = (capsStruct[21] & v4L2CapDeviceCaps) != 0 ? capsStruct[22] : capsStruct[21];
                    if ((caps & v4L2CapVideoCapture) != 0)
                    {
                        string devicePath = Marshal.PtrToStringUTF8(v4L2Device)!;

                        // Try to get a friendly name from udev properties
                        IntPtr modelName = udev_device_get_property_value(udevDevice, "ID_MODEL");
                        IntPtr vendorName = udev_device_get_property_value(udevDevice, "ID_VENDOR");

                        string friendlyName = Path.GetFileName(devicePath); // Default to filename

                        if (modelName != IntPtr.Zero)
                        {
                            string model = Marshal.PtrToStringUTF8(modelName) ?? "";
                            string vendor = "";

                            if (vendorName != IntPtr.Zero)
                            {
                                vendor = Marshal.PtrToStringUTF8(vendorName) ?? "";
                            }

                            if (!string.IsNullOrEmpty(vendor) && !string.IsNullOrEmpty(model))
                            {
                                friendlyName = $"{vendor} {model} ({Path.GetFileName(devicePath)})";
                            }
                            else if (!string.IsNullOrEmpty(model))
                            {
                                friendlyName = $"{model} ({Path.GetFileName(devicePath)})";
                            }
                        }

                        EnsureUniqueKey(cameraDict, friendlyName, devicePath);
                    }
                }
            }
            finally
            {
                if (enumerate != IntPtr.Zero)
                    udev_enumerate_unref(enumerate);
                udev_unref(udev);
            }
        }
        catch (Exception e)
        {
            cameraDict.Add($"Error listing UVC devices: {e.Message}", "error");
        }
    }

    private void AddSerialPorts(Dictionary<string, string> cameraDict)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                foreach (string port in SerialPort.GetPortNames().Distinct())
                {
                    EnsureUniqueKey(cameraDict, port, port);
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                string[] ports = Directory.GetFiles("/dev", "ttyACM*");
                foreach (string port in ports)
                {
                    EnsureUniqueKey(cameraDict, $"Serial Device {Path.GetFileName(port)}", port);
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                string[] ports = Directory.GetFiles("/dev", "tty.usb*");
                foreach (string port in ports)
                {
                    EnsureUniqueKey(cameraDict, $"Serial Device {Path.GetFileName(port)}", port);
                }
            }
        }
        catch (Exception ex)
        {
            cameraDict.Add($"Error listing serial devices: {ex.Message}", "error");
        }
    }

    private void EnsureUniqueKey(Dictionary<string, string> dict, string key, string value)
    {
        string uniqueKey = key;
        int counter = 1;

        // If the key already exists, append a number to make it unique
        while (dict.ContainsKey(uniqueKey))
        {
            uniqueKey = $"{key} ({counter})";
            counter++;
        }

        dict.Add(uniqueKey, value);
    }
}
