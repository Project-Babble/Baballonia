using Baballonia.Contracts;
using System.Collections.Generic;

namespace Baballonia;

public static class FaceAverager
{
    // Should keep the same order as in ParameterSenderService.FaceExpressionMap
    private static readonly Dictionary<string, int> faceNameIndexMap = new()
    {
        { "CheekPuffLeft", 0 },
        { "CheekPuffRight", 1 },
        { "CheekSuckLeft", 2 },
        { "CheekSuckRight", 3 },
        { "JawOpen", 4 },
        { "JawForward", 5 },
        { "JawLeft", 6 },
        { "JawRight", 7 },
        { "NoseSneerLeft", 8 },
        { "NoseSneerRight", 9 },
        { "MouthFunnel", 10 },
        { "MouthPucker", 11 },
        { "MouthLeft", 12 },
        { "MouthRight", 13 },
        { "MouthRollUpper", 14 },
        { "MouthRollLower", 15 },
        { "MouthShrugUpper", 16 },
        { "MouthShrugLower", 17 },
        { "MouthClose", 18 },
        { "MouthSmileLeft", 19 },
        { "MouthSmileRight", 20 },
        { "MouthFrownLeft", 21 },
        { "MouthFrownRight", 22 },
        { "MouthDimpleLeft", 23 },
        { "MouthDimpleRight", 24 },
        { "MouthUpperUpLeft", 25 },
        { "MouthUpperUpRight", 26 },
        { "MouthLowerDownLeft", 27 },
        { "MouthLowerDownRight", 28 },
        { "MouthPressLeft", 29 },
        { "MouthPressRight", 30 },
        { "MouthStretchLeft", 31 },
        { "MouthStretchRight", 32 },
        { "TongueOut", 33 },
        { "TongueUp", 34 },
        { "TongueDown", 35 },
        { "TongueLeft", 36 },
        { "TongueRight", 37 },
        { "TongueRoll", 38 },
        { "TongueBendDown", 39 },
        { "TongueCurlUp", 40 },
        { "TongueSquish", 41 },
        { "TongueFlat", 42 },
        { "TongueTwistLeft", 43 },
        { "TongueTwistRight", 44 },
    };

    // List of facekey pairs and their corresponding amount setting keys to average
    private static readonly (string left, string right, string amountKey)[] pairs =
    {
        ("MouthSmileLeft", "MouthSmileRight", "AppSettings_AverageSmileAmount"),
        ("MouthFrownLeft", "MouthFrownRight", "AppSettings_AverageFrownAmount"),
        ("MouthDimpleLeft", "MouthDimpleRight", "AppSettings_AverageDimpleAmount"),
        ("MouthUpperUpLeft", "MouthUpperUpRight", "AppSettings_AverageUpperUpAmount"),
        ("MouthLowerDownLeft", "MouthLowerDownRight", "AppSettings_AverageLowerDownAmount"),
        ("MouthPressLeft", "MouthPressRight", "AppSettings_AveragePressAmount"),
        ("MouthStretchLeft", "MouthStretchRight", "AppSettings_AverageStretchAmount"),
    };

    /// <summary>
    /// Apply averaging to all permitted left/right face expression pairs.
    /// expressions is a list of face expression weights ordered as defined by the faceKeys list.
    /// </summary>
    public static void ApplyAveraging(ref float[] expressionsWeights, ILocalSettingsService settings)
    {
        if (expressionsWeights == null) return;
        if (settings == null) return;

        if (!settings.ReadSetting("AppSettings_AverageMouthEnabled", false)) return;

        foreach (var (leftKey, rightKey, amountKey) in pairs)
        {
            // Get the index of each expression
            faceNameIndexMap.TryGetValue(leftKey, out int leftIndex);
            faceNameIndexMap.TryGetValue(rightKey, out int rightIndex);

            float left = expressionsWeights[leftIndex];
            float right = expressionsWeights[rightIndex];
            float avg = (left + right) / 2f;
            float amount = settings.ReadSetting(amountKey, 0f); // don't average in case of missing setting

            expressionsWeights[leftIndex] = float.Lerp(left, avg, amount);
            expressionsWeights[rightIndex] = float.Lerp(right, avg, amount);
        }
    }
}
