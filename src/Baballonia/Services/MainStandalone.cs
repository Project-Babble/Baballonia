using System;
using System.Threading.Tasks;
using AvaloniaMiaDev.Contracts;
using Microsoft.Extensions.Logging;

namespace AvaloniaMiaDev.Services;

public class MainStandalone : IMainService
{
    private readonly ILogger<MainStandalone> _logger;

    public Action<string, float> ParameterUpdate { get; set; } = (_, _) => { };

    public MainStandalone(
        ILogger<MainStandalone> logger
        )
    {
        _logger = logger;

        if (!OperatingSystem.IsWindows()) return;

        _logger.LogDebug("Setting our time begin period...");
        var timeEndRes = Utils.TimeBeginPeriod(1);
        if (timeEndRes != 0)
        {
            _logger.LogWarning($"TimeBeginPeriod failed with HRESULT {timeEndRes}");
        }
    }

    public Task Teardown()
    {
        _logger.LogInformation("Exiting!");

        if (OperatingSystem.IsWindows())
        {
            _logger.LogDebug("Resetting our time end period...");
            var timeEndRes = Utils.TimeEndPeriod(1);
            if (timeEndRes != 0)
            {
                _logger.LogWarning($"TimeEndPeriod failed with HRESULT {timeEndRes}");
            }
        }

        _logger.LogDebug("Teardown complete. Awaiting exit...");
        return Task.CompletedTask;
    }

    public Task InitializeAsync()
    {
        // Begin main OSC update loop
        _logger.LogDebug("Starting OSC update loop...");
        return Task.CompletedTask;
    }
}
