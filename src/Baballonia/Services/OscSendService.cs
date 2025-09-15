using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Microsoft.Extensions.Logging;
using OscCore;

namespace Baballonia.Services;

/// <summary>
/// OscSendService is responsible for encoding osc messages and sending them over OSC
/// </summary>
public class OscSendService
{
    private readonly ILogger<OscSendService> _logger;
    private readonly IOscTarget _oscTarget;

    private CancellationTokenSource _cts;
    private Socket _sendSocket;
    public event Action<int> OnMessagesDispatched = _ => { };

    public OscSendService(
        ILogger<OscSendService> logger,
        IOscTarget oscTarget
    )
    {
        _logger = logger;
        _cts = new CancellationTokenSource();

        _oscTarget = oscTarget;
        UpdateTarget(new IPEndPoint(IPAddress.Parse(_oscTarget.DestinationAddress), _oscTarget.OutPort));

        _oscTarget.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not nameof(IOscTarget.OutPort))
            {
                return;
            }

            if (_oscTarget.OutPort == default)
            {
                _oscTarget.OutPort = 8888;
            }

            if (_oscTarget.DestinationAddress is not null)
            {
                UpdateTarget(new IPEndPoint(IPAddress.Parse(_oscTarget.DestinationAddress), _oscTarget.OutPort));
            }
            else
            {
                _oscTarget.DestinationAddress = IPAddress.Loopback.ToString();
            }
        };
    }

    private void UpdateTarget(IPEndPoint endpoint)
    {
        _cts.Cancel();
        _sendSocket?.Close();
        _oscTarget.IsConnected = false;

        _sendSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        try
        {
            _sendSocket.Connect(endpoint);
            _oscTarget.IsConnected = true;
        }
        catch (SocketException ex)
        {
            _logger.LogWarning($"Failed to bind to sender endpoint: {endpoint}. {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Unexpected Exception while binding to sender endpoint: {endpoint}. {ex.Message}");
        }
        finally
        {
            _cts = new CancellationTokenSource();
        }
    }

    public async Task Send(OscMessage message, CancellationToken ct)
    {
        if (_sendSocket is not { Connected: true })
        {
            return;
        }

        try
        {
            var ip = IPEndPoint.Parse(_oscTarget.DestinationAddress);
            await _sendSocket.SendToAsync(message.ToByteArray(), SocketFlags.None, ip, ct);
            OnMessagesDispatched(1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending OSC message");
        }
    }

    public async Task Send(OscMessage[] messages, CancellationToken ct)
    {
        if (_sendSocket is not { Connected: true })
        {
            return;
        }

        try
        {
            foreach (var message in messages)
            {
                await _sendSocket.SendAsync(message.ToByteArray(), SocketFlags.None, ct);
            }

            OnMessagesDispatched(messages.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending OSC bundle");
        }
    }
}
