using Baballonia.Contracts;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Baballonia.Services;

public class VrcftModuleSendService : OscSendService
{
    public VrcftModuleSendService(ILogger<OscSendService> logger, IOscTarget oscTarget) : base(logger, oscTarget)
    {
        ApplyTarget();

        OscTarget.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(IOscTarget.OutPort) or nameof(IOscTarget.DestinationAddress))
                ApplyTarget();
        };
    }

    private void ApplyTarget()
    {
        if (OscTarget.OutPort == default)
            OscTarget.OutPort = 8888;

        // TryParse, not Parse: this runs during DI construction, so a malformed stored address must
        // not throw and fail host startup. Fall back to loopback and persist it.
        if (!IPAddress.TryParse(OscTarget.DestinationAddress, out var address))
        {
            address = IPAddress.Loopback;
            OscTarget.DestinationAddress = address.ToString();
        }

        UpdateTarget(new IPEndPoint(address, OscTarget.OutPort));
    }
}
