using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace Baballonia.Contracts;

public interface IDeviceEnumerator
{
    protected ILogger Logger { get; set; }
    public Dictionary<string, string> Cameras { get; set; }
    public Dictionary<string, string> UpdateCameras();
}
