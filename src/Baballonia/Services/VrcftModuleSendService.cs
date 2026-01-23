using System.Net;
using Baballonia.Contracts;
using Microsoft.Extensions.Logging;

namespace Baballonia.Services;

public class VrcftModuleSendService : OscSendService
{
    public VrcftModuleSendService(ILogger<OscSendService> logger, IOscTarget oscTarget) : base(logger, oscTarget)
    {
        UpdateTarget(new IPEndPoint(IPAddress.Parse(OscTarget.DestinationAddress), OscTarget.OutPort));

        OscTarget.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not nameof(IOscTarget.OutPort))
            {
                return;
            }

            if (OscTarget.OutPort == default)
            {
                OscTarget.OutPort = 8888;
            }

            if (OscTarget.DestinationAddress is not null)
            {
                UpdateTarget(new IPEndPoint(IPAddress.Parse(OscTarget.DestinationAddress), OscTarget.OutPort));
            }
            else
            {
                OscTarget.DestinationAddress = IPAddress.Loopback.ToString();
            }
        };
    }
}
