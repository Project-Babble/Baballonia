using Baballonia.Contracts;
using Baballonia.Helpers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OscCore;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Baballonia.Services;

/// <summary>
/// Receives OSC over UDP. The local port may be temporarily unavailable (a companion app holding it,
/// a restart in progress); binding is owned by the receive loop, which retries with backoff so the
/// path recovers on its own once the port frees up. Re-targeting closes the current socket to wake
/// an in-flight receive, then the loop rebinds.
/// </summary>
public class OscRecvService : BackgroundService
{
    private readonly ILogger<OscRecvService> _logger;
    private readonly IOscTarget _oscTarget;
    private readonly ILocalSettingsService _settingsService;

    private readonly byte[] _recvBuffer = new byte[4096];
    private readonly object _gate = new();
    private Socket? _recvSocket;
    private IPEndPoint? _desiredEndpoint;
    private long _nextBindTick;
    private bool _bindWarned;
    private const int BindBackoffMs = 1000;

    public event Action<OscMessage> OnMessageReceived = _ => { };

    public OscRecvService(
        ILogger<OscRecvService> logger,
        IOscTarget oscTarget,
        ILocalSettingsService settingsService
    )
    {
        _logger = logger;
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

            // TryParse, not Parse: this handler runs synchronously during settings load in StartAsync,
            // so a malformed stored address must degrade gracefully instead of failing host startup.
            if (IPAddress.TryParse(_oscTarget.DestinationAddress, out var address))
            {
                UpdateTarget(new IPEndPoint(address, _oscTarget.InPort));
            }
            else
            {
                _logger.LogWarning("Ignoring invalid OSC destination address: '{Address}'",
                    _oscTarget.DestinationAddress);
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
        lock (_gate)
        {
            _desiredEndpoint = endpoint;
            _nextBindTick = 0;     // rebind to the new target immediately
            _bindWarned = false;
            DropSocketLocked();    // closing the current socket wakes an in-flight ReceiveAsync
        }
        return endpoint;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("OSC Receive Service ExecuteAsync started");

        while (!stoppingToken.IsCancellationRequested)
        {
            Socket? socket;
            lock (_gate)
                socket = EnsureBoundLocked();

            if (socket is null)
            {
                await DelayQuietly(BindBackoffMs, stoppingToken);
                continue;
            }

            try
            {
                var received = await socket.ReceiveAsync(_recvBuffer, stoppingToken);
                if (received > 0)
                    Dispatch(received);
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
            catch (ObjectDisposedException)
            {
                // Socket swapped out by a re-target; the loop rebinds next iteration.
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.OperationAborted or SocketError.Interrupted)
            {
                // Socket closed mid-receive for a re-target; rebind without backoff.
            }
            catch (SocketException ex)
            {
                lock (_gate)
                    if (ReferenceEquals(_recvSocket, socket))
                        DropSocketLocked();
                _logger.LogWarning("OSC receive error: {Message}; retrying", ex.Message);
                await DelayQuietly(BindBackoffMs, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing OSC message");
            }
        }

        lock (_gate)
            DropSocketLocked();
    }

    // Caller holds _gate. Returns a bound socket, retrying the bind at most once per backoff window.
    private Socket? EnsureBoundLocked()
    {
        if (_recvSocket is { IsBound: true } bound)
            return bound;
        if (_desiredEndpoint is null)
            return null;

        var now = Environment.TickCount64;
        if (now < _nextBindTick)
            return null;
        _nextBindTick = now + BindBackoffMs;

        DropSocketLocked();
        try
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(_desiredEndpoint);
            _recvSocket = socket;
            _oscTarget.IsConnected = true;
            _bindWarned = false;
            _logger.LogInformation("OSC receive bound to {Endpoint}", _desiredEndpoint);
            return socket;
        }
        catch (SocketException ex)
        {
            DropSocketLocked();
            _oscTarget.IsConnected = false;
            if (!_bindWarned)
            {
                _bindWarned = true;
                _logger.LogWarning("Could not bind OSC receive to {Endpoint}: {Message}; will keep retrying",
                    _desiredEndpoint, ex.Message);
            }
            return null;
        }
    }

    // Caller holds _gate.
    private void DropSocketLocked()
    {
        if (_recvSocket is { } socket)
        {
            try { socket.Close(); } catch { /* ignored */ }
            socket.Dispose();
            _recvSocket = null;
        }
    }

    private void Dispatch(int received)
    {
        OscPacket packet;
        try
        {
            packet = OscPacket.Read(_recvBuffer, 0, received);
        }
        catch
        {
            return;     // ignore a malformed datagram; never blackout the receive loop over one bad packet
        }

        if (packet is OscBundle)
        {
            foreach (var message in OscHelper.ExtractMessages(packet))
                OnMessageReceived(message);
        }
        else if (packet is OscMessage message)
        {
            OnMessageReceived(message);
        }
    }

    private static async Task DelayQuietly(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct); }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
            DropSocketLocked();
        await base.StopAsync(cancellationToken);
    }
}
