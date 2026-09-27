using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Baballonia;

/// <summary>
/// BFR Link, protocol v1: lets a Babble Frame Runtime headset find this module over the network, keeps a heartbeat
/// with it, and tells <see cref="BabbleOsc"/> which addresses the headset's OSC data may come from.
///
/// Control messages are small JSON datagrams on UDP <c>LinkPort</c> (6767), each with "proto":"bfr-link","v":1:
///   beacon  headset -> broadcast   {sid, name, version}       answered with an offer
///   query   module  -> broadcast   {mid}                      on start and when a session ends; headsets beacon back
///   offer   module  -> beacon      {mid, name, data_port, busy}
///   ping    headset -> module      {sid, seq, path, active}   1 Hz on every path; answered with a pong
///   pong    module  -> ping        {mid, seq}
///   bye     either side            {sid} / {mid}
/// The headset can reach the PC over several paths at once (its streaming dongle, USB, the home LAN). Every address a
/// beacon or ping of the session came from is a valid source for its data, so a path switch needs no new handshake.
/// One headset at a time: others get busy offers until the session ends (3 s without pings or data, or a bye).
/// </summary>
public sealed class BabbleLink
{
    private const string Proto = "bfr-link";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private readonly ILogger _logger;
    private readonly int _port;
    private readonly int _dataPort;
    private readonly string _mid = Guid.NewGuid().ToString("N")[..16];
    private readonly Socket _socket;
    private readonly Thread _thread;
    private volatile bool _running = true;

    private readonly object _lock = new();
    private string? _sid;               // the current session's headset, null while listening
    private string _headsetName = "";
    private readonly Dictionary<IPAddress, string> _peers = new();   // source address -> path kind
    private string _activePath = "";
    private DateTime _lastSeen;

    /// <summary>Raised on the link thread when a session ends, so the headset's values can be reset.</summary>
    public event Action? SessionEnded;

    public BabbleLink(ILogger logger, int port, int dataPort)
    {
        _logger = logger;
        _port = port;
        _dataPort = dataPort;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _socket.EnableBroadcast = true;
        _socket.ReceiveTimeout = 250;
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        IgnoreConnectionResets(_socket);
        _logger.LogInformation($"BFR Link listening on UDP {port} for Babble Frame headsets");
        _thread = new Thread(Loop) { IsBackground = true, Name = "BFR Link" };
        _thread.Start();
        SendQuery();
    }

    /// <summary>Whether OSC data from this address belongs to the linked headset (called for every datagram).</summary>
    public bool IsPeer(IPAddress address)
    {
        lock (_lock)
        {
            if (_sid == null || !_peers.ContainsKey(address))
                return false;
            _lastSeen = DateTime.UtcNow;
            return true;
        }
    }

    public void Teardown()
    {
        _running = false;
        lock (_lock)
        {
            foreach (var peer in _peers.Keys)
                Send(new IPEndPoint(peer, _port), Message("bye"));
        }
        _socket.Close();
        _thread.Join();
    }

    private void Loop()
    {
        var buffer = new byte[2048];
        while (_running)
        {
            try
            {
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                int length;
                try
                {
                    length = _socket.ReceiveFrom(buffer, ref remote);
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut)
                {
                    CheckTimeout();
                    continue;
                }
                Handle(buffer.AsSpan(0, length), (IPEndPoint)remote);
                CheckTimeout();
            }
            catch (Exception e) when (_running)
            {
                _logger.LogDebug($"BFR Link: {e.Message}");
                Thread.Sleep(100);
            }
            catch
            {
                // Closing the socket at teardown.
            }
        }
    }

    private void Handle(ReadOnlySpan<byte> data, IPEndPoint from)
    {
        JsonObject? m;
        try
        {
            m = JsonNode.Parse(Encoding.UTF8.GetString(data)) as JsonObject;
        }
        catch (JsonException)
        {
            return;
        }
        if (m == null || Str(m, "proto") != Proto || m["v"]?.GetValueKind() != JsonValueKind.Number ||
            (int)m["v"]! != 1)
            return;
        var type = Str(m, "type");
        var sid = Str(m, "sid");
        switch (type)
        {
            case "beacon" when sid.Length > 0:
                OnBeacon(sid, Str(m, "name"), Str(m, "version"), from);
                break;
            case "ping" when sid.Length > 0:
                OnPing(sid, Str(m, "path"), m["active"]?.GetValue<bool>() ?? false, m["seq"], from);
                break;
            case "bye" when sid.Length > 0:
                lock (_lock)
                {
                    if (sid == _sid)
                        EndSession("the headset closed the link");
                }
                break;
        }
    }

    private void OnBeacon(string sid, string name, string version, IPEndPoint from)
    {
        bool busy;
        lock (_lock)
        {
            // A new session ID from an address of the current session: the headset restarted.
            if (_sid != null && sid != _sid && _peers.ContainsKey(from.Address))
                EndSession("the headset restarted");
            if (_sid == null)
            {
                _sid = sid;
                _headsetName = name;
                _peers.Clear();
                _activePath = "";
                _lastSeen = DateTime.UtcNow;
                _logger.LogInformation($"BFR Link: found headset \"{name}\" ({version}) at {from.Address}");
            }
            busy = sid != _sid;
            if (!busy)
            {
                _peers.TryAdd(from.Address, "");
                _lastSeen = DateTime.UtcNow;
            }
        }
        var offer = Message("offer");
        offer["name"] = Environment.MachineName;
        offer["data_port"] = _dataPort;
        offer["busy"] = busy;
        Send(from, offer);
    }

    private void OnPing(string sid, string path, bool active, JsonNode? seq, IPEndPoint from)
    {
        lock (_lock)
        {
            if (sid != _sid)
                return;   // not our headset (or an old session): no pong, so it looks elsewhere
            _peers[from.Address] = path;
            _lastSeen = DateTime.UtcNow;
            if (active && path != _activePath)
            {
                _logger.LogInformation(_activePath.Length == 0
                    ? $"BFR Link: connected to \"{_headsetName}\" via {path} ({from.Address})"
                    : $"BFR Link: \"{_headsetName}\" switched to {path} ({from.Address})");
                _activePath = path;
            }
        }
        var pong = Message("pong");
        pong["seq"] = seq?.DeepClone();
        Send(from, pong);
    }

    private void CheckTimeout()
    {
        lock (_lock)
        {
            if (_sid != null && DateTime.UtcNow - _lastSeen > Timeout)
                EndSession("the headset stopped answering");
        }
    }

    // Called with _lock held.
    private void EndSession(string why)
    {
        _logger.LogInformation($"BFR Link: disconnected from \"{_headsetName}\": {why}");
        _sid = null;
        _peers.Clear();
        _activePath = "";
        try
        {
            SessionEnded?.Invoke();
        }
        catch (Exception e)
        {
            _logger.LogWarning($"BFR Link: resetting values failed: {e.Message}");
        }
        SendQuery();
    }

    /// <summary>Asks headsets on every network to beacon now, so a waiting headset connects at once.</summary>
    private void SendQuery()
    {
        var query = Message("query");
        var targets = new HashSet<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    var ip = unicast.Address.GetAddressBytes();
                    var mask = unicast.IPv4Mask.GetAddressBytes();
                    var broadcast = new byte[4];
                    for (var i = 0; i < 4; i++)
                        broadcast[i] = (byte)(ip[i] | ~mask[i]);
                    targets.Add(new IPAddress(broadcast));
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogDebug($"BFR Link: listing interfaces failed: {e.Message}");
        }
        foreach (var target in targets)
            Send(new IPEndPoint(target, _port), query);
    }

    private JsonObject Message(string type) => new()
    {
        ["proto"] = Proto,
        ["v"] = 1,
        ["type"] = type,
        ["mid"] = _mid,
    };

    private void Send(IPEndPoint to, JsonObject message)
    {
        try
        {
            _socket.SendTo(Encoding.UTF8.GetBytes(message.ToJsonString()), to);
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            // An unreachable network, or teardown: the heartbeat takes care of the rest.
        }
    }

    private static string Str(JsonObject m, string key) =>
        m[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    /// <summary>
    /// On Windows, an ICMP "port unreachable" for an earlier send makes the next receive on a UDP socket throw
    /// (WSAECONNRESET). Turn that off, as the Baballonia app's OSC sender does.
    /// </summary>
    internal static void IgnoreConnectionResets(Socket socket)
    {
        if (!OperatingSystem.IsWindows())
            return;
        const int sioUdpConnReset = -1744830452;
        socket.IOControl(sioUdpConnReset, new byte[4], null);
    }
}
