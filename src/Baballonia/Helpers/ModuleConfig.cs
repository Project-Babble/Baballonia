namespace Baballonia.Helpers;

public class ModuleConfig(string host, int port, bool isEyeSupported, bool isFaceSupported)
{
	public string Host { get; } = host;
    public int Port { get; } = port;
    public bool IsEyeSupported { get; } = isEyeSupported;
    public bool IsFaceSupported { get; } = isFaceSupported;
}
