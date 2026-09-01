using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System.Text.RegularExpressions;
using System.Threading;

namespace Baballonia.SDK;

/// <summary>
/// Defines custom camera stream behavior
/// </summary>
public abstract class Capture(string source, ILogger logger) : IDisposable
{
    protected ILogger Logger = logger;
    private Mat? _rawMat;
    private object _rawMatLock = new();

    // Signalled while an unconsumed frame is available; reset once it is acquired. Lets a consumer
    // block until a fresh frame arrives instead of busy-polling AcquireRawMat. Set/Reset happen
    // under _rawMatLock so the signal state always matches "is there a frame waiting".
    private readonly ManualResetEventSlim _frameReady = new(false);

    /// <summary>
    /// A wait handle that becomes signalled when a fresh, unconsumed frame is available and is
    /// reset once <see cref="AcquireRawMat"/> takes it. Consumers can <c>WaitHandle.WaitAny</c>
    /// across several sources to pace themselves to the real capture rate. Thread safe.
    /// </summary>
    public WaitHandle FrameWaitHandle => _frameReady.WaitHandle;

    /// <summary>
    /// Where this Capture source is currently pulling data from
    /// </summary>
    public string Source { get; set; } = source;

    /// <summary>
    /// Represents the incoming frame data for this capture source.
    /// Will be `dimension` in BGR color space. <br/>
    /// Acquiring this value the caller takes ownership of the Mat object and sets the internal reference to null. <br/>
    /// Thread safe
    /// </summary>
    public Mat? AcquireRawMat()
    {
        Mat? result;
        lock (_rawMatLock)
        {
            result = _rawMat;
            _rawMat = null;
            _frameReady.Reset();
        }
        return result;
    }

    /// <summary>
    /// Sets current Mat object that can be acquired by someone else. <br/>
    /// The caller gives up the responsibility for the object <br/>
    /// It is prohibited to use the value object after calling this method <br/>
    /// Thread safe
    /// </summary>
    /// <param name="value">value</param>
    protected void SetRawMat(Mat value)
    {
        lock (_rawMatLock)
        {
            if (ReferenceEquals(_rawMat, value)) return;

            if (_rawMat != null)
            {
                // Previous frame was never acquired by the consumer — it's lost.
                _rawMat.Dispose();
                Interlocked.Increment(ref _framesDropped);
            }
            _rawMat = value;
            _frameReady.Set();
        }
        Interlocked.Increment(ref _framesProduced);
    }

    private long _framesProduced;
    private long _framesDropped;

    /// <summary>
    /// Total frames this source has produced so far (incremented once per delivered frame).
    /// Sample the delta over time to compute the real capture throughput. Thread safe.
    /// </summary>
    public long FramesProduced => Interlocked.Read(ref _framesProduced);

    /// <summary>Frames overwritten before the consumer acquired them — frames actually lost (not in-flight). Thread safe.</summary>
    public long FramesDropped => Interlocked.Read(ref _framesDropped);

    /// <summary>Negotiated capture frame rate if the backend knows it (0 = unknown).</summary>
    public virtual double TargetFps => 0;

    /// <summary>Human-readable pixel format if the backend knows it (empty = unknown).</summary>
    public virtual string PixelFormatName => "";

    /// <summary>
    /// Is this Capture source ready to produce data?
    /// </summary>
    public bool IsReady { get; protected set; } = false;

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

    public virtual void Dispose(){}
}
