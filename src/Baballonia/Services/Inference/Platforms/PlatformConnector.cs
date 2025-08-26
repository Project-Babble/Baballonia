using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Helpers;
using Baballonia.Services.Inference.Models;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Capture = Baballonia.SDK.Capture;

namespace Baballonia.Services.Inference.Platforms;

/// <summary>
/// Manages what Captures are allowed to run on what platforms, as well as their Urls, etc.
/// </summary>
public abstract class PlatformConnector
{
    protected ILogger Logger { get; }
    protected ILocalSettingsService LocalSettingsService { get; }

    public string Source { get; private set; }

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
    public Dictionary<Capture, Type> Captures;

    public PlatformConnector(string source, ILogger logger, ILocalSettingsService localSettingsService)
    {
        Source = source;
        Logger = logger;
        LocalSettingsService = localSettingsService;
    }

    /// <summary>
    /// Initializes a Platform Connector
    /// </summary>
    public virtual bool Initialize(string source)
    {
        if (string.IsNullOrEmpty(source)) return false;

        this.Source = source;

        try
        {
            foreach (var capture in Captures
                         .Where(capture => capture.Key.CanConnect(source)))
            {
                Capture = (Capture)Activator.CreateInstance(capture.Value, source)!;
                Logger.LogInformation($"Changed capture source to {capture.Value.Name} with url {source}.");
                break;
            }

            if (Capture is null) return false;

            Capture.StartCapture();
            Logger.LogInformation($"Starting {Capture.GetType().Name} capture source...");
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
            throw;
        }

        return false;
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

        fixed (float* array = floatArray)
        {
            using var finalMat = Mat.FromPixelData(size.Height, size.Width, MatType.CV_32F, new IntPtr(array));
            // settings.Brightness = 1.0f / 255.0f;
            return TransformRawImage(finalMat, settings);
        }
    }

    public unsafe bool TransformRawImage(Mat outputMat, CameraSettings settings)
    {
        if (Capture?.RawMat == null || !Capture.IsReady)
            return false;

        if (Capture.RawMat.DataPointer == null ||
            Capture.RawMat.Width <= 0 || Capture.RawMat.Height <= 0 || Capture.RawMat.Channels() <= 0)
            return false;

        var sourceMat = Capture.RawMat;

        int roiX = (int)settings.Roi.X;
        int roiY = (int)settings.Roi.Y;
        int roiWidth = (int)settings.Roi.Width;
        int roiHeight = (int)settings.Roi.Height;
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

        // If needed, convert float Mats to Byte mats and normallize
        if (resultMat.Type() != outputMat.Type())
        {
            var newMat = new Mat();
            resultMat.ConvertTo(newMat, outputMat.Type(), 1f / 255f);
            resultMat.Dispose();
            resultMat = newMat;
        }

        // Adjust brightness
        if (settings.Gamma is < 0.48f or > 0.52f)
        {
            var newMat = new Mat();
            resultMat.ConvertTo(newMat, outputMat.Type(), settings.Gamma.Remap(0f, 1f, 0.5f, 2f));
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
