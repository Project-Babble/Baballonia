using Baballonia.Contracts;
using Microsoft.Extensions.Logging;
using OscCore;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Baballonia.Services;

/// <summary>
/// OscSendService encodes OSC messages and sends them over UDP. The consumer (VRChat / VRCFT module /
/// DFR) can start, stop, or restart at any time relative to us; the socket is (re)established lazily
/// and a send failure never tears the path down, so output resumes automatically when the consumer
/// comes back.
/// </summary>
public abstract class OscSendService
{
    public event Action<int> OnMessagesDispatched = _ => { };
    protected readonly IOscTarget OscTarget;

    private readonly ILogger<OscSendService> _logger;
    private readonly object _socketLock = new();
    private Socket? _sendSocket;
    private IPEndPoint? _endpoint;
    private long _nextConnectTick;
    private long _lastErrorLogTick;
    private const int ConnectBackoffMs = 1000;
    private const int ErrorLogIntervalMs = 5000;
    private const int SioUdpConnReset = unchecked((int)0x9800000C);

    protected OscSendService(ILogger<OscSendService> logger, IOscTarget oscTarget)
    {
        _logger = logger;
        OscTarget = oscTarget;
    }

    protected void UpdateTarget(IPEndPoint endpoint)
    {
        lock (_socketLock)
        {
            _endpoint = endpoint;
            _nextConnectTick = 0;
            CloseSocketLocked();
            TryConnectLocked();
        }
    }

    private void CloseSocketLocked()
    {
        if (_sendSocket is { } socket)
        {
            try { socket.Close(); } catch { /* ignored */ }
            socket.Dispose();
            _sendSocket = null;
        }
        OscTarget.IsConnected = false;
    }

    // Connectionless UDP: Connect() only records the destination, so it succeeds whether or not the
    // consumer is up. Rebuilds at most once per backoff window when the socket is missing.
    private Socket? EnsureSocketLocked()
    {
        if (_sendSocket is { } existing)
            return existing;
        if (_endpoint is null)
            return null;

        var now = Environment.TickCount64;
        if (now < _nextConnectTick)
            return null;
        _nextConnectTick = now + ConnectBackoffMs;
        TryConnectLocked();
        return _sendSocket;
    }

    private void TryConnectLocked()
    {
        if (_endpoint is null)
            return;

        Socket? socket = null;
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            // On Windows a missing consumer's ICMP port-unreachable would otherwise fault the next
            // send; suppress it so sends silently no-op until the consumer (re)starts.
            if (OperatingSystem.IsWindows())
                socket.IOControl(SioUdpConnReset, new byte[4], null);
            socket.Connect(_endpoint);
            _sendSocket = socket;
            OscTarget.IsConnected = true;
        }
        catch (Exception ex)
        {
            socket?.Dispose();
            _sendSocket = null;
            OscTarget.IsConnected = false;
            LogThrottled("Failed to connect OSC sender to {Endpoint}: {Message}", _endpoint, ex.Message);
        }
    }

    public async Task Send(OscMessage message, CancellationToken ct)
    {
        Socket? socket;
        lock (_socketLock)
            socket = EnsureSocketLocked();
        if (socket is null)
            return;

        try
        {
            await socket.SendAsync(message.ToByteArray(), SocketFlags.None, ct);
            OnMessagesDispatched(1);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            OnSendFailed(socket, ex);
        }
    }

    public async Task Send(OscMessage[] messages, CancellationToken ct)
    {
        Socket? socket;
        lock (_socketLock)
            socket = EnsureSocketLocked();
        if (socket is null)
            return;

        var sent = 0;
        try
        {
            foreach (var message in messages)
            {
                await socket.SendAsync(message.ToByteArray(), SocketFlags.None, ct);
                sent++;
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            OnSendFailed(socket, ex);
        }

        if (sent > 0)
            OnMessagesDispatched(sent);
    }

    // A connected UDP socket stays usable across a consumer restart, so transient send errors are
    // logged (rate-limited) and swallowed; only a disposed socket is torn down so the next send rebuilds.
    private void OnSendFailed(Socket socket, Exception ex)
    {
        if (ex is ObjectDisposedException)
        {
            lock (_socketLock)
                if (ReferenceEquals(_sendSocket, socket))
                    CloseSocketLocked();
            return;
        }

        LogThrottled("Error sending OSC message: {Message}", ex.Message);
    }

    private void LogThrottled(string template, params object?[] args)
    {
        var now = Environment.TickCount64;
        if (now - _lastErrorLogTick < ErrorLogIntervalMs)
            return;
        _lastErrorLogTick = now;
        _logger.LogWarning(template, args);
    }
}
