using Microsoft.Extensions.Hosting;

namespace VRCFaceTracking.Baballonia;

public class Config
{
	public string Host { get; }
    public int Port { get; }
    /// <summary>Claim eye tracking in VRCFaceTracking. A linked headset sets this to whether its eye tracking is on.</summary>
    public bool IsEyeSupported { get; set; }
    /// <summary>Claim face tracking in VRCFaceTracking. A linked headset sets this to whether its mouth tracking is on.</summary>
    public bool IsFaceSupported { get; set; }
    /// <summary>BFR Link: lets a Babble Frame headset find this module on the network (default on).</summary>
    public bool LinkEnabled { get; }
    /// <summary>BFR Link control port (UDP, default 6767).</summary>
    public int LinkPort { get; }

    public Config(string host, int port, bool? isEyeSupported = null, bool? isFaceSupported = null,
        bool? linkEnabled = null, int? linkPort = null)
    {
        Host = host;
        Port = port;
        IsEyeSupported = isEyeSupported ?? false;
        IsFaceSupported = isFaceSupported ?? true;
        LinkEnabled = linkEnabled ?? true;
        LinkPort = linkPort ?? 6767;
    }
}
