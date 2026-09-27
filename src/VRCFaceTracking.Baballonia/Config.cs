using Microsoft.Extensions.Hosting;

namespace VRCFaceTracking.Baballonia;

public class Config
{
	public string Host { get; }
    public int Port { get; }
    public bool IsEyeSupported { get; }
    public bool IsFaceSupported { get; }
    /// <summary>BFR Link: lets a Babble Frame headset find this module on the network (default on).</summary>
    public bool LinkEnabled { get; }
    /// <summary>BFR Link control port (UDP, default 6767).</summary>
    public int LinkPort { get; }

    public Config(string host, int port, bool isEyeSupported, bool isFaceSupported, bool? linkEnabled = null,
        int? linkPort = null)
    {
        Host = host;
        Port = port;
        IsEyeSupported = isEyeSupported;
        IsFaceSupported = isFaceSupported;
        LinkEnabled = linkEnabled ?? true;
        LinkPort = linkPort ?? 6767;
    }
}
