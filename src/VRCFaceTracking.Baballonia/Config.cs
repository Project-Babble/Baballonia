using Microsoft.Extensions.Hosting;

namespace VRCFaceTracking.Baballonia;

public class Config
{
	public string Host { get; }
    public int Port { get; }
    public bool IsEyeSupported { get; }
    public bool IsFaceSupported { get; }

    public Config(string host, int port, bool isEyeSupported, bool isFaceSupported)
    {
        Host = host;
        Port = port;
        IsEyeSupported = isEyeSupported;
        IsFaceSupported = isFaceSupported;
    }
}
