using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Baballonia.Contracts;

public interface IDeviceEnumerator
{
    protected ILogger Logger { get; set; }
    public Dictionary<string, string> Cameras { get; set; }
    public Dictionary<string, string> UpdateCameras();
}
