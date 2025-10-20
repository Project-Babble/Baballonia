namespace Baballonia.SDK;

public interface ICaptureFactory
{
    public Capture Create(string address);
    public bool CanConnect(string address);
    public string GetProviderName();
}
