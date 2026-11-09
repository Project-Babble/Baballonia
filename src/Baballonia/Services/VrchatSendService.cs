using Baballonia.Contracts;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Baballonia.Services;

/// <summary>
/// Sends to VRChat's OSC input, for VRChat's native eye and face tracking endpoints.
/// </summary>
public class VrchatSendService : OscSendService
{
    private const int VrchatPort = 9000;

    public VrchatSendService(ILogger<OscSendService> logger, IOscTarget oscTarget) : base(logger, oscTarget)
    {
        UpdateTarget(new IPEndPoint(IPAddress.Loopback, VrchatPort));
    }
}
