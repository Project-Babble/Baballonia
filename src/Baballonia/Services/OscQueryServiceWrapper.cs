using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VRC.OSCQuery;

namespace Baballonia.Services;

public class OscQueryServiceWrapper(ILogger<OscQueryServiceWrapper> logger, ILocalSettingsService localSettingsService)
    : BackgroundService, IDisposable
{
    private readonly HashSet<OSCQueryServiceProfile> _profiles = [];
    private OSCQueryService _serviceWrapper = null!;

    private static readonly Regex VrChatClientRegex = new(@"VRChat-Client-[A-Za-z0-9]{6}$", RegexOptions.Compiled);
    private CancellationTokenSource _cancellationTokenSource;
    private const int VrcPort = 9000;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ipString = localSettingsService.ReadSetting<string>("OSCAddress");
        var hostIp = IPAddress.Parse(ipString);

        _cancellationTokenSource = new CancellationTokenSource();
        var tcpPort = Extensions.GetAvailableTcpPort();
        var udpPort = Extensions.GetAvailableUdpPort();

        _serviceWrapper = new OSCQueryServiceBuilder()
            .WithDiscovery(new MeaModDiscovery())
            .WithHostIP(hostIp)
            .WithTcpPort(tcpPort)
            .WithUdpPort(udpPort)
            .WithServiceName(
                $"VRChat-Client-BabbleApp-{Utils.RandomString()}") // Yes this has to start with "VRChat-Client" https://github.com/benaclejames/VRCFaceTracking/blob/f687b143037f8f1a37a3aabf97baa06309b500a1/VRCFaceTracking.Core/mDNS/MulticastDnsService.cs#L195
            .StartHttpServer()
            .AdvertiseOSCQuery()
            .AdvertiseOSC()
            .Build();

        logger.LogInformation(
            $"[VRCFTReceiver] Started OSCQueryService {_serviceWrapper.ServerName} at TCP {tcpPort}, UDP {udpPort}, HTTP http://{_serviceWrapper.HostIP}:{tcpPort}");

        _serviceWrapper.AddEndpoint<string>("/avatar/change", Attributes.AccessValues.ReadWrite, ["default"]);
        _serviceWrapper.OnOscQueryServiceAdded += AddProfileToList;

        StartAutoRefreshServices(5000);

        return Task.CompletedTask;
    }

    private void AddProfileToList(OSCQueryServiceProfile profile)
    {
        if (_profiles.Contains(profile) || profile.port == _serviceWrapper.TcpPort)
        {
            return;
        }
        _profiles.Add(profile);
        logger.LogInformation($"Added {profile.name} to list of OSCQuery profiles, at address http://{profile.address}:{profile.port}");
    }

    private void StartAutoRefreshServices(double interval)
    {
        logger.LogInformation("OSCQuery start StartAutoRefreshServices");

        Task.Run(async () =>
        {
            while (true)
            {
                var useOscQuery = localSettingsService.ReadSetting<bool>("UseOSCQuery");
                if (useOscQuery)
                {
                    try
                    {
                        _serviceWrapper.RefreshServices();
                        PollVrChatParameters();
                    }
                    catch (Exception)
                    {
                        // Ignore
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(interval));
            }
        });
    }

    private void PollVrChatParameters()
    {
        if (_profiles.Count == 0) return;

        try
        {
            var vrcProfile = _profiles.First(profile => VrChatClientRegex.IsMatch(profile.name));

            var hostIp = localSettingsService.ReadSetting<string>("OSCAddress");
            var vrcIp = vrcProfile.address.ToString();
            if (hostIp != vrcIp)
            {
                localSettingsService.SaveSetting("OSCAddress", vrcIp);
            }

            var hostPort = localSettingsService.ReadSetting<int>("OSCOutPort");
            if (hostPort != VrcPort)
            {
                localSettingsService.SaveSetting("OSCOutPort", VrcPort);
            }
        }
        catch (InvalidOperationException)
        {
            // No matching element, continue
        }
        catch (Exception ex)
        {
            logger.LogError($"Unhandled error in OSCQueryService: {ex}");
        }
    }


    public override void Dispose()
    {
        logger.LogInformation("OSCQuery teardown called");
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        _serviceWrapper.Dispose();
        logger.LogInformation("OSCQuery teardown completed");
    }
}
