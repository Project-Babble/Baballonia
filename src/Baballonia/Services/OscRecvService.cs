using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Helpers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OscCore;

namespace Baballonia.Services;

public class OscRecvService : BackgroundService
{
    private readonly ILogger<OscRecvService> _logger;
    private readonly IOscTarget _oscTarget;
    private readonly ILocalSettingsService _settingsService;

    private Socket _recvSocket;
    private readonly byte[] _recvBuffer = new byte[4096];

    private CancellationTokenSource _cts, _linkedToken;
    private CancellationToken _stoppingToken;

    public event Action<OscMessage> OnMessageReceived = _ => { };

    public OscRecvService(
        ILogger<OscRecvService> logger,
        IOscTarget oscTarget,
        ILocalSettingsService settingsService
    )
    {
        _logger = logger;
        _cts = new CancellationTokenSource();

        _oscTarget = oscTarget;
        _settingsService = settingsService;

        _oscTarget.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not nameof(IOscTarget.InPort))
            {
                return;
            }

            if (_oscTarget.InPort == default)
            {
                return;
            }

            if (_oscTarget.DestinationAddress is not null)
            {
                UpdateTarget(new IPEndPoint(IPAddress.Parse(_oscTarget.DestinationAddress), _oscTarget.InPort));
            }
        };
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting OSC Receive Service...");
        _settingsService.Load(_oscTarget);
        _logger.LogDebug("OSC target loaded - Address: {Address}, InPort: {InPort}", _oscTarget.DestinationAddress,
            _oscTarget.InPort);
        await base.StartAsync(cancellationToken);
        _logger.LogDebug("OSC Receive Service started successfully");
    }

    public IPEndPoint UpdateTarget(IPEndPoint endpoint)
    {
        _cts.Cancel();
        _recvSocket?.Close();
        _oscTarget.IsConnected = false;

        _recvSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        try
        {
            _recvSocket.Bind(endpoint);
            _oscTarget.IsConnected = true;
            return (IPEndPoint)_recvSocket.LocalEndPoint!;
        }
        catch (SocketException ex)
        {
            _logger.LogWarning($"Could not bind to recv endpoint: {endpoint}. {ex.Message}");
        }
        finally
        {
            _cts = new CancellationTokenSource();
            _linkedToken = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken, _cts.Token);
        }

        return null!;
    }

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("OSC Receive Service ExecuteAsync started");
        _stoppingToken = stoppingToken;
        _linkedToken = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken, _cts.Token);

        while (!_stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_linkedToken.IsCancellationRequested || _recvSocket is not { IsBound: true })
                {
                    await Task.Delay(100, stoppingToken);
                    continue;
                }

                var bytesReceived = await _recvSocket.ReceiveAsync(_recvBuffer, _linkedToken.Token);
                OscPacket packet = OscPacket.Read(_recvBuffer, 0, bytesReceived);

                if (bytesReceived == 0) continue;

                if (packet is OscBundle)
                {
                    List<OscMessage> allMessages = OscHelper.ExtractMessages(packet);

                    foreach (var message in allMessages)
                    {
                        OnMessageReceived(message);
                    }
                }
                else if (packet is OscMessage message)
                {
                    OnMessageReceived(message);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown or target updates
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing OSC message");
                await Task.Delay(1000, stoppingToken); // Prevent tight loop on errors
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        _recvSocket?.Close();
        await base.StopAsync(cancellationToken);
    }
}
