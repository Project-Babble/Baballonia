using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using VRCFaceTracking.Core.OSC;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Baballonia;

public class BabbleOsc
{

    private Socket? _receiver;

    private bool _loop = true;

    private readonly Thread? _thread;

    private readonly int _resolvedPort;

    private readonly string? _resolvedHost;

    private const string DefaultHost = "127.0.0.1";

    private const int DefaultPort = 8888;

    private const int TimeoutMs = 10000;

    private const float MinPupilMm = 2f;

    private const float MaxPupilMm = 8f;

    private readonly BabbleLink? _link;

    private readonly bool _openToNetwork;

    private readonly object _applyLock = new();

    private readonly HashSet<string> _linkAddresses = new();

    private volatile bool _eye;

    private volatile bool _expression;

    /// <summary>OSC address of the (re)initialization request: two booleans, eye tracking then face tracking.</summary>
    public const string InitAddress = "/vrcft/initialize";

    /// <summary>Raised on the listener thread when a local sender (the Baballonia app) asks for eye and face tracking.</summary>
    public event Action<bool, bool>? InitRequested;

    /// <param name="link">With a BFR Link, the socket listens on every interface for senders on this machine (the
    /// Baballonia app); the headset's data comes through the link instead. Without one it listens on
    /// <paramref name="host"/> only.</param>
    /// Nothing is claimed until <see cref="SetClaims"/>, so values are dropped until then; the socket still listens, so
    /// an <see cref="InitAddress"/> request can arrive while the module decides what to claim.
    public BabbleOsc(ILogger iLogger, string host, int? port, BabbleLink? link = null)
    {
        _link = link;
        // "0.0.0.0" set by hand: accept any sender, as before the link existed.
        _openToNetwork = host == "0.0.0.0";
        if (_receiver != null)
        {
            iLogger.LogError("BabbleEyeOSC connection already exists.");
            return;
        }
        _resolvedHost = host ?? DefaultHost;
        _resolvedPort = port ?? DefaultPort;

        iLogger.LogInformation(_link != null
            ? $"Started BabbleEyeOSC on port {_resolvedPort} (this machine{(_openToNetwork ? " and any sender" : "")}; a BFR Link headset sends through the link)"
            : $"Started BabbleEyeOSC with Host: {_resolvedHost} and Port {_resolvedPort}");
        ConfigureReceiver();
        _loop = true;
        _thread = new Thread(ListenLoop);
        _thread.Start();
    }

    /// <summary>Sets what this module claimed in VRCFaceTracking: eye values are dropped unless <paramref name="eye"/>,
    /// face values unless <paramref name="expression"/>.</summary>
    public void SetClaims(bool eye, bool expression)
    {
        _eye = eye;
        _expression = expression;
    }

    private void ConfigureReceiver()
    {
        // Only local senders here (the headset's data comes through the link), so the configured host as given:
        // 127.0.0.1, or "0.0.0.0" to take any sender.
        IPAddress address = IPAddress.Parse(_resolvedHost!);
        IPEndPoint localEp = new IPEndPoint(address, _resolvedPort);
        _receiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _receiver.Bind(localEp);
        BabbleLink.IgnoreConnectionResets(_receiver);
        _receiver.ReceiveTimeout = TimeoutMs;
    }

    private void ListenLoop()
    {
        byte[] array = new byte[4096];
        while (_loop)
        {
            try
            {
                if (_receiver!.IsBound)
                {
                    EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    int len = _receiver.ReceiveFrom(array, ref remote);
                    IPAddress sender = ((IPEndPoint)remote).Address;
                    if (!IPAddress.IsLoopback(sender) && !_openToNetwork)
                        continue;   // not this machine
                    if (OscPacket.TryReadTwoBools(array.AsSpan(0, len), out var address, out var eye, out var face))
                    {
                        if (address == InitAddress)
                            InitRequested?.Invoke(eye, face);
                        continue;
                    }
                    int messageIndex = 0;
                    OscMessage oscMessage;
                    try
                    {
                        oscMessage = new OscMessage(array, len, ref messageIndex);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (oscMessage.Value is float value)
                    {
                        Apply(oscMessage.Address, value, fromLink: false);
                    }
                }
                else
                {
                    _receiver.Close();
                    _receiver.Dispose();
                    ConfigureReceiver();
                }
            }
            catch (Exception)
            {
                // ignore
            }
        }
    }

    /// <summary>
    /// Applies one OSC value. Values from the linked headset are recorded, so they can be reset when the link ends
    /// (merged per expression with a local sender such as the Baballonia app: the latest value wins).
    /// </summary>
    public void Apply(string address, float value, bool fromLink)
    {
        // Only write what this module claimed in Initialize: the rest belongs to other modules. Every eye address
        // (gaze, lid, widen, squint, brow, pupil) has "Eye" in it, and no face address does.
        bool isEye = address.Contains("Eye", StringComparison.OrdinalIgnoreCase);
        if (isEye ? !_eye : !_expression)
            return;

        lock (_applyLock)
        {
            if (fromLink)
                _linkAddresses.Add(address);
            else
                _linkAddresses.Remove(address);   // a local sender owns it now

            switch (address)
            {
                /* mouth params */ 
                case "/cheekPuffLeft":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.CheekPuffLeft].Weight = value;
                    break;
                case "/cheekPuffRight":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.CheekPuffRight].Weight = value;
                    break;
                case "/cheekSuckLeft":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.CheekSuckLeft].Weight = value;
                    break;
                case "/cheekSuckRight":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.CheekSuckRight].Weight = value;
                    break;
                case "/jawOpen":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.JawOpen].Weight = value;
                    break;
                case "/jawForward":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.JawForward].Weight = value;
                    break;
                case "/jawLeft":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.JawLeft].Weight = value;
                    break;
                case "/jawRight":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.JawRight].Weight = value;
                    break;
                case "/noseSneerLeft":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.NoseSneerLeft].Weight = value;
                    break;
                case "/noseSneerRight":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.NoseSneerRight].Weight = value;
                    break;
                case "/mouthFunnel":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.LipFunnelLowerLeft].Weight = value;
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.LipFunnelLowerRight].Weight = value;
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.LipFunnelUpperLeft].Weight = value;
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.LipFunnelUpperRight].Weight = value;
                    break;
                case "/mouthPucker":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.LipPuckerLowerLeft].Weight = value;
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.LipPuckerLowerRight].Weight = value;
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.LipPuckerUpperLeft].Weight = value;
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.LipPuckerUpperRight].Weight = value;
                    break;
                case "/mouthLeft":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthUpperLeft].Weight = value;
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthLowerLeft].Weight = value;
                    break;
                case "/mouthRight":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthUpperRight].Weight = value;
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthLowerRight].Weight = value;
                    break;
                case "/mouthRollUpper":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.LipSuckUpperLeft].Weight = value;
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.LipSuckUpperRight].Weight = value;
                    break;
                case "/mouthRollLower":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.LipSuckLowerLeft].Weight = value;
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.LipSuckLowerRight].Weight = value;
                    break;
                case "/mouthShrugUpper":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthRaiserUpper].Weight = value;
                    break;
                case "/mouthShrugLower":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthRaiserLower].Weight = value;
                    break;
                case "/mouthClose":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthClosed].Weight = value;
                    break;
                case "/mouthSmileLeft":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthCornerPullLeft].Weight = value;
                    break;
                case "/mouthSmileRight":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthCornerPullRight].Weight = value;
                    break;
                case "/mouthFrownLeft":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthFrownLeft].Weight = value;
                    break;
                case "/mouthFrownRight":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthFrownRight].Weight = value;
                    break;
                case "/mouthDimpleLeft":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthDimpleLeft].Weight = value;
                    break;
                case "/mouthDimpleRight":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthDimpleRight].Weight = value;
                    break;
                case "/mouthUpperUpLeft":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthUpperUpLeft].Weight = value;
                    break;
                case "/mouthUpperUpRight":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthUpperUpRight].Weight = value;
                    break;
                case "/mouthLowerDownLeft":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthLowerDownLeft].Weight = value;
                    break;
                case "/mouthLowerDownRight":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthLowerDownRight].Weight = value;
                    break;
                case "/mouthPressLeft":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthPressLeft].Weight = value;
                    break;
                case "/mouthPressRight":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthPressRight].Weight = value;
                    break;
                case "/mouthStretchLeft":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthStretchLeft].Weight = value;
                    break;
                case "/mouthStretchRight":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthStretchRight].Weight = value;
                    break;
                case "/tongueOut":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.TongueOut].Weight = value;
                    break;
                case "/tongueUp":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.TongueUp].Weight = value;
                    break;
                case "/tongueDown":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.TongueDown].Weight = value;
                    break;
                case "/tongueLeft":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.TongueLeft].Weight = value;
                    break;
                case "/tongueRight":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.TongueRight].Weight = value;
                    break;
                case "/tongueRoll":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.TongueRoll].Weight = value;
                    break;
                case "/tongueBendDown":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.TongueBendDown].Weight = value;
                    break;
                case "/tongueCurlUp":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.TongueCurlUp].Weight = value;
                    break;
                case "/tongueSquish":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.TongueSquish].Weight = value;
                    break;
                case "/tongueFlat":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.TongueFlat].Weight = value;
                    break;
                case "/tongueTwistLeft":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.TongueTwistLeft].Weight = value;
                    break;
                case "/tongueTwistRight":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.TongueTwistRight].Weight = value;
                    break;


                /* eye params */
                case "/LeftEyeX":
                case "/leftEyeX":
                    UnifiedTracking.Data.Eye.Left.Gaze.x = value;
                    break;
                case "/LeftEyeY":
                case "/leftEyeY":
                    UnifiedTracking.Data.Eye.Left.Gaze.y = value;
                    break;
                case "/LeftEyeLid":
                case "/leftEyeLid":
                    UnifiedTracking.Data.Eye.Left.Openness = value;
                    break;
                case "/LeftEyeWiden":
                case "/leftEyeWiden":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight = value;
                    break;
                case "/LeftEyeSquint":
                case "/leftEyeSquint":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.EyeSquintLeft].Weight = value;
                    break;
                case "/LeftEyeBrow":
                case "/leftEyeBrow":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.BrowLowererLeft].Weight = value;
                    break;
                case "/RightEyeX":
                case "/rightEyeX":
                    UnifiedTracking.Data.Eye.Right.Gaze.x = value;
                    break;
                case "/RightEyeY":
                case "/rightEyeY":
                    UnifiedTracking.Data.Eye.Right.Gaze.y = value;
                    break;
                case "/RightEyeLid":
                case "/rightEyeLid":
                    UnifiedTracking.Data.Eye.Right.Openness = value;
                    break;
                case "/RightEyeWiden":
                case "/rightEyeWiden":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.EyeWideRight].Weight = value;
                    break;
                case "/RightEyeSquint":
                case "/rightEyeSquint":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.EyeSquintRight].Weight = value;
                    break;
                case "/RightEyeBrow":
                case "/rightEyeBrow":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.BrowLowererRight].Weight = value;
                    break;

                /* pupil dilation. VRCFaceTracking normalizes PupilDiameter_MM across the smallest and largest
                   diameters it has seen to make v2/PupilDilation, and exports the diameters themselves as
                   v2/PupilDiameterLeft / v2/PupilDiameterRight (mm * 0.1). Senders that measure a real diameter
                   use the *PupilDiameter addresses; senders that only have a normalized 0..1 dilation (0 =
                   constricted, 1 = dilated) use the *Dilation ones and are spread over a nominal human 2..8 mm. */
                case "/LeftEyePupilDiameter":
                case "/leftEyePupilDiameter":
                    UnifiedTracking.Data.Eye.Left.PupilDiameter_MM = value;
                    break;
                case "/RightEyePupilDiameter":
                case "/rightEyePupilDiameter":
                    UnifiedTracking.Data.Eye.Right.PupilDiameter_MM = value;
                    break;
                case "/CombinedEyePupilDiameter":
                case "/combinedEyePupilDiameter":
                    UnifiedTracking.Data.Eye.Left.PupilDiameter_MM = value;
                    UnifiedTracking.Data.Eye.Right.PupilDiameter_MM = value;
                    break;
                case "/LeftEyeDilation":
                case "/leftEyeDilation":
                    UnifiedTracking.Data.Eye.Left.PupilDiameter_MM = DilationToMm(value);
                    break;
                case "/RightEyeDilation":
                case "/rightEyeDilation":
                    UnifiedTracking.Data.Eye.Right.PupilDiameter_MM = DilationToMm(value);
                    break;
                case "/CombinedEyeDilation":
                case "/combinedEyeDilation":
                    UnifiedTracking.Data.Eye.Left.PupilDiameter_MM = DilationToMm(value);
                    UnifiedTracking.Data.Eye.Right.PupilDiameter_MM = DilationToMm(value);
                    break;

                /* combined eye params (single value driving both eyes) */
                case "/CombinedEyeWiden":
                case "/combinedEyeWiden":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight = value;
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.EyeWideRight].Weight = value;
                    break;
                case "/CombinedEyeSquint":
                case "/combinedEyeSquint":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.EyeSquintLeft].Weight = value;
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.EyeSquintRight].Weight = value;
                    break;
                case "/CombinedEyeBrow":
                case "/combinedEyeBrow":
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.BrowLowererLeft].Weight = value;
                    UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.BrowLowererRight].Weight = value;
                    break;
            }
        }
    }

    /// <summary>Resets what the linked headset had set to neutral (open eyes, centred gaze, relaxed face).</summary>
    public void ResetLinkValues()
    {
        string[] addresses;
        lock (_applyLock)
            addresses = _linkAddresses.ToArray();
        foreach (var address in addresses)
        {
            if (address.Contains("Pupil") || address.Contains("Dilation"))
                continue;   // no neutral pupil size; VRCFaceTracking normalises it anyway
            Apply(address, address.EndsWith("EyeLid") ? 1f : 0f, fromLink: false);
        }
        lock (_applyLock)
            _linkAddresses.Clear();
    }

    /// <summary>
    /// A normalized 0..1 dilation (0 = constricted, 1 = dilated) as a pupil diameter in millimetres, over the nominal
    /// human range. Only the span matters: VRCFaceTracking rescales whatever it receives to the min and max it has seen.
    /// </summary>
    private static float DilationToMm(float dilation)
    {
        return Math.Clamp(dilation, 0f, 1f).Remap(0f, 1f, MinPupilMm, MaxPupilMm);
    }

    public void Teardown()
    {
        _loop = false;
        _receiver!.Close();
        _receiver.Dispose();
        _thread!.Join();
    }
}
