using System;
using Baballonia.Contracts;
using Usb.Events;

namespace Baballonia.Services;

public sealed class UsbService : IUsbService
{
    public event Action<string>? OnUsbConnected;
    public event Action<string>? OnUsbDisconnected;

    private static readonly IUsbEventWatcher UsbEventWatcher = new UsbEventWatcher(
        startImmediately: true,
        addAlreadyPresentDevicesToList: true,
        usePnPEntity: false,
        includeTTY: true);

    private readonly TimeSpan _eventThrottleInterval = TimeSpan.FromSeconds(1);
    private DateTime _lastEventTime = DateTime.MinValue;
    private readonly object _eventLock = new();

    public UsbService()
    {
        UsbEventWatcher.UsbDeviceAdded += UsbDeviceAdded;
        UsbEventWatcher.UsbDeviceRemoved += UsbDeviceRemoved;
    }

    private void UsbDeviceAdded(object? sender, UsbDevice? device)
    {
        if (device == null)
            return;

        RateLimitedAction(device.DeviceName, OnUsbConnected);
    }

    private void UsbDeviceRemoved(object? sender, UsbDevice? device)
    {
        if (device == null)
            return;

        RateLimitedAction(device.DeviceName, OnUsbDisconnected);
    }

    private void RateLimitedAction(string deviceName, Action<string>? action)
    {
        lock (_eventLock)
        {
            var now = DateTime.UtcNow;
            var timeSinceLastEvent = now - _lastEventTime;

            if (timeSinceLastEvent < _eventThrottleInterval)
            {
                return;
            }

            _lastEventTime = now;
        }

        action?.Invoke(deviceName);
    }

    ~UsbService()
    {
        UsbEventWatcher.UsbDeviceAdded -= UsbDeviceAdded;
        UsbEventWatcher.UsbDeviceRemoved -= UsbDeviceRemoved;
        UsbEventWatcher.Dispose();
    }
}
