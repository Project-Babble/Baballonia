using OscCore;
using System;
using System.Collections.Generic;

namespace Baballonia.Services;

/// <summary>
/// Builds the payload for VRChat's native face tracking: one byte (0-255) per tracking key, in VRChat's key order.
/// See https://vrc-beta-docs.netlify.app/avatars/face-tracking/osc/
/// </summary>
public class VrcFaceTrackingPayload
{
    public const string Address = "/tracking/face/v1";
    public const int Length = 102;

    private readonly float[] _keys = new float[Length];

    // Baballonia expression name -> VRChat tracking key indices. Mirrors the VRCFaceTracking.Baballonia module mapping.
    private static readonly Dictionary<string, int[]> FaceKeys = new()
    {
        { "CheekPuffLeft", [33] },
        { "CheekPuffRight", [32] },
        { "CheekSuckLeft", [35] },
        { "CheekSuckRight", [34] },
        { "JawOpen", [36] },
        { "JawForward", [39] },
        { "JawLeft", [38] },
        { "JawRight", [37] },
        { "NoseSneerLeft", [63] },
        { "NoseSneerRight", [62] },
        { "MouthFunnel", [50, 51, 52, 53] }, // LipFunnel Upper/Lower Right/Left
        { "MouthPucker", [54, 55, 56, 57] }, // LipPucker Upper/Lower Right/Left
        { "MouthLeft", [67, 69] }, // MouthUpperLeft, MouthLowerLeft
        { "MouthRight", [66, 68] }, // MouthUpperRight, MouthLowerRight
        { "MouthRollUpper", [44, 45] }, // LipSuckUpper Right/Left
        { "MouthRollLower", [46, 47] }, // LipSuckLower Right/Left
        { "MouthShrugUpper", [80] }, // MouthRaiserUpper
        { "MouthShrugLower", [81] }, // MouthRaiserLower
        { "MouthClose", [43] }, // MouthClosed
        { "MouthSmileLeft", [71] }, // MouthCornerPullLeft
        { "MouthSmileRight", [70] }, // MouthCornerPullRight
        { "MouthFrownLeft", [75] },
        { "MouthFrownRight", [74] },
        { "MouthDimpleLeft", [79] },
        { "MouthDimpleRight", [78] },
        { "MouthUpperUpLeft", [59] },
        { "MouthUpperUpRight", [58] },
        { "MouthLowerDownLeft", [65] },
        { "MouthLowerDownRight", [64] },
        { "MouthPressLeft", [83] },
        { "MouthPressRight", [82] },
        { "MouthStretchLeft", [77] },
        { "MouthStretchRight", [76] },
        { "TongueOut", [86] },
        { "TongueUp", [87] },
        { "TongueDown", [88] },
        { "TongueLeft", [90] },
        { "TongueRight", [89] },
        { "TongueRoll", [91] },
        { "TongueBendDown", [92] },
        { "TongueCurlUp", [93] },
        { "TongueSquish", [94] },
        { "TongueFlat", [95] },
        { "TongueTwistLeft", [97] },
        { "TongueTwistRight", [96] },
    };

    /// <summary>
    /// Sets the eye keys. Gaze is -1 to 1 (positive is right/up), openness is 0 (closed) to 1 (open).
    /// </summary>
    public void SetEyes(float leftX, float leftY, float leftOpenness, float rightX, float rightY, float rightOpenness)
    {
        _keys[0] = rightX; // EyeLookOutRight
        _keys[1] = -rightX; // EyeLookInRight
        _keys[2] = rightY; // EyeLookUpRight
        _keys[3] = -rightY; // EyeLookDownRight
        _keys[4] = -leftX; // EyeLookOutLeft
        _keys[5] = leftX; // EyeLookInLeft
        _keys[6] = leftY; // EyeLookUpLeft
        _keys[7] = -leftY; // EyeLookDownLeft
        _keys[8] = 1f - rightOpenness; // EyeClosedRight
        _keys[9] = 1f - leftOpenness; // EyeClosedLeft
    }

    public void ClearEyes() => Array.Clear(_keys, 0, 10);

    public void SetFace(string name, float value)
    {
        if (!FaceKeys.TryGetValue(name, out var keys))
            return;

        foreach (var key in keys)
            _keys[key] = value;
    }

    public OscMessage ToMessage()
    {
        var bytes = new byte[Length];
        for (var i = 0; i < Length; i++)
            bytes[i] = (byte)(Math.Clamp(_keys[i], 0f, 1f) * 255);

        return new OscMessage(Address, bytes);
    }
}
