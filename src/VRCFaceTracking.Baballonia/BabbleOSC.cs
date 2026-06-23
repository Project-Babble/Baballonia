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

    public BabbleOsc(ILogger iLogger, string host, int? port)
    {
        if (_receiver != null)
        {
            iLogger.LogError("BabbleEyeOSC connection already exists.");
            return;
        }
        _resolvedHost = host ?? DefaultHost;
        _resolvedPort = port ?? TimeoutMs;

        iLogger.LogInformation($"Started BabbleEyeOSC with Host: {_resolvedHost} and Port {_resolvedPort}");
        ConfigureReceiver();
        _loop = true;
        _thread = new Thread(ListenLoop);
        _thread.Start();
    }

    private void ConfigureReceiver()
    {
        IPAddress address = IPAddress.Parse(_resolvedHost!);
        IPEndPoint localEp = new IPEndPoint(address, _resolvedPort);
        _receiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _receiver.Bind(localEp);
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
                    int len = _receiver.Receive(array);
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
                        switch (oscMessage.Address)
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

    public void Teardown()
    {
        _loop = false;
        _receiver!.Close();
        _receiver.Dispose();
        _thread!.Join();
    }
}
