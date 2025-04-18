using System.Threading.Tasks;
using OpenCvSharp;

namespace AvaloniaMiaDev.Services.Inference.Captures;

/// <summary>
/// Defines custom camera stream behavior
/// </summary>
public abstract class Capture
{
    /// <summary>
    /// Represents the size of a "default" Babble frame
    /// Pulled from the Babble Board ESP32 cam
    /// </summary>
    public virtual (int width, int height) DefaultFrameDimensions { get; } = (240, 240);

    /// <summary>
    /// Empty frame, used when data is bad and we need to return something
    /// </summary>
    protected virtual Mat EmptyMat => Mat.Zeros(DefaultFrameDimensions.width, DefaultFrameDimensions.height, MatType.CV_32F, 3);

    public abstract string Url { get; set; }

    public abstract uint FrameCount { get; protected set; }

    /// <summary>
    /// Represents the incoming frame data for this capture source.
    /// Will be `dimension` in BGR color space
    /// </summary>
    public abstract Mat RawMat { get;  }

    /// <summary>
    /// Dimensions for this frame. Needs to be explicitly defined when dealing with
    /// custom capture sources (IE Serial/Cameras)
    /// </summary>
    public abstract (int width, int height) Dimensions { get; }

    /// <summary>
    /// Is this Capture source ready to produce data?
    /// </summary>
    public abstract bool IsReady { get; protected set; }

    /// <summary>
    /// Start Capture on this source
    /// </summary>
    /// <returns></returns>
    public abstract Task<bool> StartCapture();

    /// <summary>
    /// Stop Capture on this source
    /// </summary>
    /// <returns></returns>
    public abstract Task<bool> StopCapture();

    public Capture(string url)
    {
        this.Url = url;
        IsReady = false;
    }
}
