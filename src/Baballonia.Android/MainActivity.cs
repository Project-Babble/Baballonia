using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware.Usb;
using Android.OS;
using Android.Util;
using Avalonia;
using Avalonia.Android;
using Baballonia.Android.Calibration;
using Baballonia.Android.Captures;
using Baballonia.Android.Receivers;
using Baballonia.Android.Services;
using Baballonia.Contracts;
using Baballonia.Services;
using Baballonia.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace Baballonia.Android;

[Activity(
    Label = "The Babble App",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode,
    Exported = true,
    LaunchMode = LaunchMode.SingleTop)]
public class MainActivity : AvaloniaMainActivity<App>
{
    private PermissionManager _permissionManager;

    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        try
        {
            Log.Info("MainActivity", "MainActivity OnCreate started");

            // Initialize permission manager
            _permissionManager = new PermissionManager(this);

            // Subscribe to USB device events
            UsbDeviceReceiver.DeviceStateChanged += OnUsbDeviceStateChanged;

            Log.Info("MainActivity", "MainActivity OnCreate completed - starting permission requests");

            // Start permission requests asynchronously (don't wait)
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000); // Give Avalonia time to fully initialize
                    await _permissionManager.RequestAllPermissionsAsync();
                }
                catch (Exception ex)
                {
                    Log.Error("MainActivity", $"Error in permission request task: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error("MainActivity", $"Error in OnCreate: {ex.Message}");
        }
    }

    protected override void OnDestroy()
    {
        try
        {
            Log.Info("MainActivity", "MainActivity OnDestroy");

            // Unsubscribe from events
            UsbDeviceReceiver.DeviceStateChanged -= OnUsbDeviceStateChanged;

            // Dispose permission manager
            _permissionManager?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("MainActivity", $"Error in OnDestroy: {ex.Message}");
        }

        base.OnDestroy();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        App.RegisterRequiredPlatformServices<
            AndroidOverlayTrainerCombo,
            AndroidDeviceEnumerator,
            AndroidConnector
        >();

        try
        {
            return base.CustomizeAppBuilder(builder)
                .WithInterFont();
        }
        catch (Exception ex)
        {
            Log.Error("MainActivity", $"Error in CustomizeAppBuilder: {ex.Message}");
            throw;
        }
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        try
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            _permissionManager?.HandlePermissionResult(requestCode, permissions, grantResults);
        }
        catch (Exception ex)
        {
            Log.Error("MainActivity", $"Error handling permission result: {ex.Message}");
        }
    }

    // Handle USB device attach/detach events
    protected override void OnNewIntent(Intent intent)
    {
        try
        {
            base.OnNewIntent(intent);

            if (UsbManager.ActionUsbDeviceAttached.Equals(intent.Action))
            {
                var device = (UsbDevice)intent.GetParcelableExtra(UsbManager.ExtraDevice);
                OnUsbDeviceStateChanged(device, true);
            }
            else if (UsbManager.ActionUsbDeviceDetached.Equals(intent.Action))
            {
                var device = (UsbDevice)intent.GetParcelableExtra(UsbManager.ExtraDevice);
                OnUsbDeviceStateChanged(device, false);
            }
        }
        catch (Exception ex)
        {
            Log.Error("MainActivity", $"Error handling new intent: {ex.Message}");
        }
    }

    private void OnUsbDeviceStateChanged(UsbDevice device, bool isAttached)
    {
        try
        {
            if (isAttached && device != null)
            {
                Log.Info("MainActivity", $"USB device attached: {device.DeviceName}");

                // Handle new device asynchronously
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _permissionManager.HandleNewUsbDeviceAsync(device);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("MainActivity", $"Error handling new USB device: {ex.Message}");
                    }
                });
            }
            else if (!isAttached && device != null)
            {
                Log.Info("MainActivity", $"USB device detached: {device.DeviceName}");
            }
        }
        catch (Exception ex)
        {
            Log.Error("MainActivity", $"Error in USB device state change handler: {ex.Message}");
        }
    }
}
