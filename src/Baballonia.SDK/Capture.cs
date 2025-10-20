using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace Baballonia.SDK;

/// <summary>
/// Defines custom camera stream behavior
/// </summary>
public abstract class Capture(string source, ILogger logger) : IDisposable
{
    protected ILogger Logger = logger;
    private Mat? _rawMat;
    private object _rawMatLock = new();

    /// <summary>
    /// Checks if the specified connection string can be used to open this device
    /// </summary>
    /// <param name="connectionString">The connection string to check</param>
    /// <returns>True if the connection string can be used to open this device, false otherwise</returns>
    public abstract bool CanConnect(string connectionString);

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

            _rawMat?.Dispose();
            _rawMat = value;
        }
    }
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
