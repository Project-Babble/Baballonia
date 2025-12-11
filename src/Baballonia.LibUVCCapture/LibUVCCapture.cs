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
    public override bool CanConnect(string connectionString) => connectionString.StartsWith("/dev/video");

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
                var formatIndex = formats.FindIndex(i => i is { Width: > 256, Height: > 256 });
                if (formatIndex == -1)
                {
                    open.Dispose();
                    _device.Dispose();
                    _device = null;
                    _context.Dispose();
                    _context = null;
                    return false;
                }
                var format = formats[formatIndex];
                open.GetStreamControlFormatSize(format.Format, format.Width, format.Height, format.Fps, out var control);
                try
                {
                    open.StartStreaming(ref control, Callback);
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
            _deviceHandle = open;
            _connected = true;
            return true;
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
        if (!_connected) return;
        if (frame.FrameFormat is not FrameFormat.Mjpeg) return;
        var data = frame.GetData();
        if (data.Length == 0) return;
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
