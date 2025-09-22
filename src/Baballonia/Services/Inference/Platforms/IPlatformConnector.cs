
using Baballonia.SDK;

namespace Baballonia.Services.Inference.Platforms;

public interface IPlatformConnector
{
    public ICaptureFactory[] GetCaptureFactories();

}
