using System.Net;
using Baballonia.Contracts;
using Microsoft.Extensions.Logging;

namespace Baballonia.Services;

public class DfrSendService : OscSendService
{
    public DfrSendService(ILogger<OscSendService> logger, IOscTarget oscTarget) : base(logger, oscTarget)
    {
        UpdateTarget(new IPEndPoint(IPAddress.Loopback, 9020));
    }
}
