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
/// OscSendService is responsible for encoding osc messages and sending them over OSC
/// </summary>
public abstract class OscSendService(
    ILogger<OscSendService> logger,
    IOscTarget oscTarget)
{
    public event Action<int> OnMessagesDispatched = _ => { };
    protected readonly IOscTarget OscTarget = oscTarget;
    private Socket _sendSocket;
    private bool _connected;
    private IPEndPoint? _sendEndpoint;

    protected void UpdateTarget(IPEndPoint endpoint)
    {
        _sendSocket?.Close();
        OscTarget.IsConnected = false;
        _connected = false;
        _sendEndpoint = null;

        _sendSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _sendEndpoint = endpoint;
        _connected = true;
        OscTarget.IsConnected = true;
    }

    public async Task Send(OscMessage message, CancellationToken ct)
    {
        if (!_connected || _sendEndpoint is null)
        {
            return;
        }

        try
        {
            await _sendSocket.SendToAsync(message.ToByteArray(), SocketFlags.None, _sendEndpoint, ct);
            OnMessagesDispatched(1);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending OSC message");
        }
    }

    public async Task Send(OscMessage[] messages, CancellationToken ct)
    {
        if (!_connected || _sendEndpoint is null)
        {
            return;
        }

        try
        {
            foreach (var message in messages)
            {
                await _sendSocket.SendToAsync(message.ToByteArray(), SocketFlags.None, _sendEndpoint, ct);
            }

            OnMessagesDispatched(messages.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending OSC bundle");
        }
    }
}
