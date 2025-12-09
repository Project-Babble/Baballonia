using System;
using Baballonia.SDK;
using Microsoft.Extensions.Logging;

namespace Baballonia.LibUVCCapture;

public class LibUVCCaptureFactory : ICaptureFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public LibUVCCaptureFactory(ILoggerFactory loggerFactory) => _loggerFactory = loggerFactory;

    public Capture Create(string address) => new LibUVCCapture(address, _loggerFactory.CreateLogger<LibUVCCapture>());

    public bool CanConnect(string address) => address.StartsWith("/dev/video");

    public string GetProviderName() => nameof(LibUVCCapture);
}
