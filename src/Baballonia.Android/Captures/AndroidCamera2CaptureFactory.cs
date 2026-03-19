using Baballonia.IPCameraCapture;
using Baballonia.SDK;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Baballonia.Android.Captures;

public class AndroidCamera2CaptureFactory : ICaptureFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public AndroidCamera2CaptureFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public bool CanConnect(string address)
    {
        return int.TryParse(address, out _);
    }

    public Capture Create(string address)
    {
        return new AndroidCamera2Capture(address, _loggerFactory.CreateLogger<AndroidCamera2Capture>());
    }

    public string GetProviderName()
    {
        return nameof(AndroidCamera2Capture);
    }
}
