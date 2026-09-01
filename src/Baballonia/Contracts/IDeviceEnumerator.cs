using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace Baballonia.Contracts;

public interface IDeviceEnumerator
{
    protected ILogger Logger { get; set; }
    public Dictionary<string, string> Cameras { get; set; }
    public Dictionary<string, string> UpdateCameras();

    /// <summary>
    /// True if the given camera friendly-name or resolved address is a Vive Facial Tracker, so the
    /// VFT backend (USB activation + YUYV decode) must be used instead of a generic camera backend
    /// that would open the un-activated device and mis-decode its image. Populated by UpdateCameras.
    /// </summary>
    public bool IsViveFacialTracker(string address) => false;
}
