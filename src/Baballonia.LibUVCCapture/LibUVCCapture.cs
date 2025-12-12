using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baballonia.SDK;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Uvc.Net;

namespace Baballonia.LibUVCCapture;

public sealed class LibUVCCapture(string source, ILogger<LibUVCCapture> logger) : Capture(source, logger)
{
    private Context _context;
    private Device _device;
    private DeviceHandle _deviceHandle;
    private bool _connected;

    public override Task<bool> StartCapture()
    {
        _context = new Context();
        _device = FindDeviceByPath(Source, _context);
        if (_device is null)
        {
            _context.Dispose();
            _context = null;
            return Task.FromResult(false);
        }
        return Task.Run(() =>
        {
            var open = _device.Open();
            try
            {
                var formats = open.GetStreamControlFormats().ToArray().OrderBy(i => i.Width).ToList();
                var formatIndex = formats.FindIndex(i => i is { Width: > 256, Height: > 256, Format: FrameFormat.Mjpeg });
                if (formatIndex == -1)
                {
                    Logger.LogInformation("Couldn't find format index");
                    open.Dispose();
                    _device.Dispose();
                    _device = null;
                    _context.Dispose();
                    _context = null;
                    return false;
                }
                Logger.LogInformation("Found format index");
                var format = formats[formatIndex];
                var control = open.GetStreamControlFormatSize(format.Format, format.Width, format.Height, 0);
                try
                {
                    Logger.LogInformation("Starting stream");
                    open.StartStreaming(ref control, Callback);
                    Logger.LogInformation("Started stream");
                    _deviceHandle = open;
                    _connected = true;
                    Logger.LogInformation("Blah");
                    return true;
                }
                catch
                {
                    open.StopStreaming();

                    open.Dispose();
                    _device.Dispose();
                    _device = null;
                    _context.Dispose();
                    _context = null;
                    return false;
                }
            }
            catch
            {
                open.Dispose();
                _device?.Dispose();
                _device = null;
                _context?.Dispose();
                _context = null;
                return false;
            }
        });
    }

    public override Task<bool> StopCapture()
    {
        _connected = false;
        if (_deviceHandle is null) return Task.FromResult(true);

        _deviceHandle.StopStreaming();
        _deviceHandle.Dispose();
        _deviceHandle = null;

        _context.Dispose();
        _context = null;

        return Task.FromResult(true);
    }

    private void Callback(ref Frame frame, IntPtr userPtr)
    {
        Logger.LogInformation("Callback called");
        if (!_connected) return;
        Logger.LogInformation("IsConnected");
        if (frame.FrameFormat is not FrameFormat.Mjpeg) return;
        Logger.LogInformation("CorrectFormat");
        var data = frame.GetData();
        if (data.Length == 0) return;
        Logger.LogInformation("HasData");
        SetRawMat(Mat.FromImageData(data));
    }

    public static Device FindDeviceByPath(string path, Context context)
    {
        if (!path.Contains("/dev/video")) return null;
        var videoIndex = path.Replace("/dev/", "");
        var ueventFilePath = $"/sys/class/video4linux/{videoIndex}/device/uevent";
        if (!File.Exists(ueventFilePath)) return null;
        var ueventText = File.ReadAllLines(ueventFilePath);
        var line = ueventText.FirstOrDefault(i => i.StartsWith("PRODUCT="));
        if (line is null) return null;
        var numbers = line.Replace("PRODUCT=", "").Split('/').Select(i => Convert.ToUInt16(i, 16)).ToArray();
        if (numbers.Length < 2) return null;
        var vendor = numbers[0];
        var product = numbers[1];
        return context.FindDevice(vendor, product);
    }
}
