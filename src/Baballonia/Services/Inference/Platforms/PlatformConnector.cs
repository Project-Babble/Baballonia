using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Services.Inference.Captures;
using Baballonia.Services.Inference.Models;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace Baballonia.Services.Inference.Platforms;

/// <summary>
/// Manages what Captures are allowed to run on what platforms, as well as their Urls, etc.
/// </summary>
public abstract class PlatformConnector
{
    protected ILogger Logger { get; }
    protected ILocalSettingsService LocalSettingsService { get; }

    /// <summary>
    /// The path to where the "data" lies
    /// </summary>
    public string Url { get; private set; }

    /// <summary>
    /// A Platform may have many Capture sources, but only one may ever be active at a time.
    /// This represents the current (and a valid) Capture source for this Platform
    /// </summary>
    public Capture? Capture { get; private set; }

    /// <summary>
    /// Dynamic collection of Capture types, their identifying strings as well as prefix/suffix controls
    /// Add (or remove) from this collection to support platform specific connectors at runtime
    /// Or support weird hardware setups
    /// </summary>
    public Dictionary<(HashSet<string> strings, bool areSuffixes), Type> Captures;

    protected abstract Type DefaultCapture { get; }

    private uint _lastFrameCount = 0;

    public PlatformConnector(string url, ILogger logger, ILocalSettingsService localSettingsService)
    {
        Url = url;
        Logger = logger;
        LocalSettingsService = localSettingsService;
    }

    /// <summary>
    /// Initializes a Platform Connector
    /// </summary>
    public virtual void Initialize(string url)
    {
        if (string.IsNullOrEmpty(url)) return;

        this.Url = url;
        foreach (var capture in Captures)
        {
            if (capture.Key.Item2)
            {
                if (capture.Key.Item1.Any(prefix => url.EndsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    Capture = (Capture)Activator.CreateInstance(capture.Value, url)!;
                    Logger.LogInformation($"Changed capture source to {capture.Value.Name} with url {url}.");
                    break;
                }
            }
            else
            {
                if (capture.Key.Item1.Any(prefix => url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    Capture = (Capture)Activator.CreateInstance(capture.Value, url)!;
                    Logger.LogInformation($"Changed capture source to {capture.Value.Name} with url {url}.");
                    break;
                }
            }
        }

        if (Capture is null)
        {
            Capture = (Capture)Activator.CreateInstance(DefaultCapture, url)!;
            Logger.LogInformation($"Defaulting capture source to {DefaultCapture.Name} with url {url}.");
        }

        if (Capture is not null)
        {
            Capture.StartCapture();
            Logger.LogInformation($"Starting {Capture.GetType().Name} capture source...");
        }
    }

    /// <summary>
    /// Converts Capture.Frame into something Babble can understand
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public unsafe bool ExtractFrameData(Span<float> floatArray, Size size, CameraSettings settings)
    {
        // Check if capture is ready and has valid data
        if (Capture?.IsReady != true || Capture.RawMat == null || Capture.RawMat.DataPointer == null || 
            Capture.RawMat.Width <= 0 || Capture.RawMat.Height <= 0)
        {
            Logger.LogWarning("Invalid or empty frame detected; skipping frame processing.");
            return false;
        }
        
        // Removed frame count check to always update the buffer
        if (floatArray.Length < size.Width * size.Height)
            throw new ArgumentException("Bad floatArray size");

        uint currentFrameCount = Capture.FrameCount;
        Logger.LogDebug($"Processing frame with count: {currentFrameCount}");

        fixed (float* array = floatArray)
        {
            using var finalMat = Mat<float>.FromPixelData(size.Height, size.Width, new IntPtr(array));
            settings.Brightness = 1.0f / 255.0f;
            bool result = TransformRawImage(finalMat, settings);
            if (result)
            {
                _lastFrameCount = currentFrameCount;
                Logger.LogDebug("Frame successfully processed and transformed");
            }
            else
            {
                Logger.LogWarning("TransformRawImage failed; skipping inference for this frame.");
            }
            return result;
        }
    }

    public unsafe bool TransformRawImage(Mat outputMat, CameraSettings settings)
    {
        if (Capture?.IsReady != true || Capture.RawMat == null || Capture.RawMat.DataPointer == null || 
            Capture.RawMat.Width <= 0 || Capture.RawMat.Height <= 0 || Capture.RawMat.Channels() <= 0)
            return false;

        var sourceMat = Capture.RawMat;

        int roiX = settings.RoiX;
        int roiY = settings.RoiY;
        int roiWidth = settings.RoiWidth;
        int roiHeight = settings.RoiHeight;
        int maxWidth = sourceMat.Width;
        int maxHeight = sourceMat.Height;

        // Ensure ROI is within bounds
        Rect roi;
        if (roiX < 0 || roiY < 0 || roiWidth <= 0 || roiHeight <= 0 ||
            roiX + roiWidth > maxWidth || roiY + roiHeight > maxHeight ||
            roiWidth == maxWidth || roiHeight == maxHeight)
        {
            roi = new Rect(0, 0, maxWidth, maxHeight);
        }
        else
        {
            roi = new Rect(roiX, roiY, roiWidth, roiHeight);
        }

        using var roiMat = new Mat(sourceMat, roi);
        Mat resultMat = roiMat.Clone();

        // Convert to grayscale or extract red channel
        if (resultMat.Channels() >= 2)
        {
            var newMat = new Mat();
            if (settings.UseRedChannel)
                Cv2.ExtractChannel(resultMat, newMat, 0);
            else
                Cv2.CvtColor(resultMat, newMat, ColorConversionCodes.BGR2GRAY);

            resultMat.Dispose();
            resultMat = newMat;
        }

        // Adjust brightness and type conversion
        if (resultMat.Type() != outputMat.Type() || settings.Brightness != 1)
        {
            var newMat = new Mat();
            resultMat.ConvertTo(newMat, outputMat.Type(), settings.Brightness);
            resultMat.Dispose();
            resultMat = newMat;
        }

        Size targetSize = outputMat.Size();
        double rotationRadians = settings.RotationRadians;
        bool hFlip = settings.UseHorizontalFlip;
        bool vFlip = settings.UseVerticalFlip;

        if (rotationRadians != 0 || hFlip || vFlip)
        {
            double cos = Math.Cos(rotationRadians), sin = Math.Sin(rotationRadians);
            double scale = 1.0 / (Math.Abs(cos) + Math.Abs(sin));
            double hscale = (hFlip ? -1.0 : 1.0) * scale;
            double vscale = (vFlip ? -1.0 : 1.0) * scale;

            using var matrix = new Mat<double>(2, 3);
            Span<double> data = matrix.AsSpan<double>();

            data[0] = (double)targetSize.Width / resultMat.Width * cos * hscale;
            data[1] = (double)targetSize.Height / resultMat.Height * sin * hscale;
            data[2] = ((double)targetSize.Width - ((double)targetSize.Width * cos + (double)targetSize.Height * sin) * hscale) * 0.5;

            data[3] = -(double)targetSize.Width / resultMat.Width * sin * vscale;
            data[4] = (double)targetSize.Height / resultMat.Height * cos * vscale;
            data[5] = ((double)targetSize.Height + ((double)targetSize.Width * sin - (double)targetSize.Height * cos) * vscale) * 0.5;

            Cv2.WarpAffine(resultMat, outputMat, matrix, targetSize);
        }
        else
        {
            try
            {
                Cv2.Resize(resultMat, outputMat, targetSize);
            }
            catch
            {
                resultMat.Dispose();
                return false;
            }
        }

        resultMat.Dispose();
        return true;
    }


    /// <summary>
    /// Shuts down the current Capture source
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public virtual void Terminate()
    {
        if (Capture is null)
        {
            // Nothing to terminate
            return;
        }

        Logger.LogInformation("Stopping capture source...");
        Capture.StopCapture();
    }
}
