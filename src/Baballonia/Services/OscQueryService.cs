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
using OscCore;
using VRC.OSCQuery;

namespace Baballonia.Services;

/// <summary>
/// Find the first VRChat OSCQuery server on a local networkw
/// </summary>
/// <param name="logger"></param>
/// <param name="oscRecvService"></param>
/// <param name="localSettingsService"></param>
/// <param name="vrChatService"></param>
public class OscQueryService(
    ILogger<OscQueryService> logger,
    OscSendService oscSendService,
    ILocalSettingsService localSettingsService,
    VRCFaceTrackingService vrChatService
    )
    : BackgroundService
{
    private readonly HashSet<OSCQueryServiceProfile> _profiles = [];
    private OSCQueryService _serviceWrapper = null!;
    private OSCQueryServiceProfile _vrchatProfile;

    private static readonly Regex VrChatClientRegex = new(@"VRChat-Client-[A-Za-z0-9]{6}$", RegexOptions.Compiled);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!localSettingsService.ReadSetting<bool>("VRC_UseVRCFaceTracking")) return Task.CompletedTask;
        localSettingsService.SaveSetting("OSCOutPort", 9000);
        localSettingsService.SaveSetting("OSCInPort", 9001);

        var tcpPort = Extensions.GetAvailableTcpPort();
        var udpPort = Extensions.GetAvailableUdpPort();

        _serviceWrapper = new OSCQueryServiceBuilder()
            .WithDiscovery(new MeaModDiscovery())
            .WithHostIP(IPAddress.Loopback)
            .WithTcpPort(tcpPort)
            .WithUdpPort(udpPort)
            .WithServiceName(
                $"VRChat-Client-BabbleApp-{Utils.RandomString()}") // Yes this has to start with "VRChat-Client" https://github.com/benaclejames/VRCFaceTracking/blob/f687b143037f8f1a37a3aabf97baa06309b500a1/VRCFaceTracking.Core/mDNS/MulticastDnsService.cs#L195
            .StartHttpServer()
            .AdvertiseOSCQuery()
            .AdvertiseOSC()
            .Build();

        logger.LogInformation(
            $"Started OSCQueryService {_serviceWrapper.ServerName} at TCP {tcpPort}, UDP {udpPort}, HTTP http://{_serviceWrapper.HostIP}:{tcpPort}");

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

    /// <summary>
    /// Polls VRChat clients until we have found one
    /// This needs to run for the duration of the app's lifetime
    /// </summary>
    /// <returns></returns>
    private void StartAutoRefreshServices(double interval = 5000)
    {
        logger.LogInformation("OSCQuery start StartAutoRefreshServices");

        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    _serviceWrapper.RefreshServices();
                    // /avatar/change doesn't really work on Android, so
                    // we'll just poll every 5000ms
                    if (ScanForVRChatClients())
                    {
                        var addr = _vrchatProfile.address;
                        oscSendService.UpdateTarget(new IPEndPoint(addr, 9000));
                        vrChatService.PullParametersFromOSCAddress(addr.ToString(), _vrchatProfile.port);
                    }
                }
                catch (Exception)
                {
                    // Ignore
                }
                await Task.Delay(TimeSpan.FromMilliseconds(interval));
            }
        });
    }

    private bool ScanForVRChatClients()
    {
        // No clients count
        if (_vrchatProfile != null) return true;
        if (_profiles.Count == 0) return false;

        try
        {
            // Only set the VRC profile once
            _vrchatProfile = _profiles.First(profile => VrChatClientRegex.IsMatch(profile.name));
            return true;
        }
        catch (InvalidOperationException)
        {
            // No matching element, continue
        }
        catch (Exception ex)
        {
            logger.LogError($"Unhandled error in OSCQueryService: {ex}");
        }

        return false;
    }

    public override void Dispose()
    {
        localSettingsService.SaveSetting("OSCAddress", "127.0.0.1");
        localSettingsService.SaveSetting("OSCInPort", 8888);
        localSettingsService.SaveSetting("OSCOutPort", 8889);

        if (_serviceWrapper == null) return;

        _serviceWrapper.OnOscQueryServiceAdded -= AddProfileToList;
        _serviceWrapper.Dispose();
    }
}
