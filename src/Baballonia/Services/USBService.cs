using System;
using Baballonia.Contracts;
using Microsoft.Extensions.Logging;
using Usb.Events;

namespace Baballonia.Services;

public sealed class UsbService : IUsbService
{
    public event Action<string>? OnUsbConnected;
    public event Action<string>? OnUsbDisconnected;

    private static readonly IUsbEventWatcher UsbEventWatcher = new UsbEventWatcher(
        startImmediately: true,                 // True - This part is obvious
        addAlreadyPresentDevicesToList: false,  // False - This part is less obvious.
                                                // Don't check devices that are already plugged in! This will spam the refresh queue on launch
        usePnPEntity: false,                    // False - PnP entity is slower, overkill for our use case
        includeTTY: true);                      // True - Legacy Babble Trackers show up under /dev/ttyACM*

    private readonly TimeSpan _eventThrottleInterval = TimeSpan.FromSeconds(3);
    private DateTime _lastEventTime = DateTime.MinValue;
    private readonly ILogger<UsbService> _logger;

    public UsbService(ILogger<UsbService> logger)
    {
        _logger = logger;
        _logger.LogDebug("Creating UsbService...");
        UsbEventWatcher.UsbDeviceAdded += UsbDeviceAdded;
        UsbEventWatcher.UsbDeviceRemoved += UsbDeviceRemoved;
    }

    private void UsbDeviceAdded(object? sender, UsbDevice? device)
    {
        // Note: Sometimes, this won't fire when a device is connected
        if (device == null)
            return;

        _logger.LogDebug("Usb added: {DeviceDeviceName}", device.DeviceName);
        RateLimitedAction(device.DeviceName, OnUsbConnected);
    }

    private void UsbDeviceRemoved(object? sender, UsbDevice? device)
    {
        // Note: Sometimes, this will fire twice when a device is disconnected
        if (device == null)
            return;

        _logger.LogDebug("Usb removed."); // DeviceName and other properties here are null/empty
        RateLimitedAction(string.Empty, OnUsbDisconnected);
    }

    private void RateLimitedAction(string deviceName, Action<string>? action)
    {
        var now = DateTime.UtcNow;
        var timeSinceLastEvent = now - _lastEventTime;

        if (timeSinceLastEvent < _eventThrottleInterval)
            return;

        _lastEventTime = now;
        _logger.LogDebug("Firing rate-limited refresh event...");
        action?.Invoke(deviceName);
    }

    ~UsbService()
    {
        _logger.LogDebug("Destroying UsbService...");
        UsbEventWatcher.UsbDeviceAdded -= UsbDeviceAdded;
        UsbEventWatcher.UsbDeviceRemoved -= UsbDeviceRemoved;
        UsbEventWatcher.Dispose();
    }
}
