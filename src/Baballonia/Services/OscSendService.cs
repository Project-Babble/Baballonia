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
public abstract class OscSendService(
    ILogger<OscSendService> logger,
    IOscTarget oscTarget)
{
    public event Action<int> OnMessagesDispatched = _ => { };
    protected readonly IOscTarget OscTarget = oscTarget;
    private Socket _sendSocket;

    protected void UpdateTarget(IPEndPoint endpoint)
    {
        _sendSocket?.Close();
        OscTarget.IsConnected = false;

        _sendSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        try
        {
            _sendSocket.Connect(endpoint);
            OscTarget.IsConnected = true;
        }
        catch (SocketException ex)
        {
            logger.LogWarning("Failed to bind to sender endpoint: {IpEndPoint}. {ExMessage}", endpoint, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError("Unexpected Exception while binding to sender endpoint: {IpEndPoint}. {ExMessage}", endpoint, ex.Message);
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
            var ip = IPEndPoint.Parse(OscTarget.DestinationAddress);
            await _sendSocket.SendToAsync(message.ToByteArray(), SocketFlags.None, ip, ct);
            OnMessagesDispatched(1);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending OSC message");
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
            logger.LogError(ex, "Error sending OSC bundle");
        }
    }
}
