using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Baballonia.Contracts;
using Baballonia.Services.Calibration;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Baballonia.Services;

public class CalibrationService : ICalibrationService
{
    private const float AutoCalSeed = 0.5f;

    // Expression parameter names
    private readonly Dictionary<string, string> _eyeExpressionMap = new()
    {
        { "LeftEyeX", "/LeftEyeX" },
        { "LeftEyeY", "/LeftEyeY" },
        { "RightEyeX", "/RightEyeX" },
        { "RightEyeY", "/RightEyeY" },
    };

    private readonly Dictionary<string, string> _faceExpressionMap = new()
    {
        { "LeftEyeLid", "/LeftEyeLid" },
        { "LeftEyeWiden", "/LeftEyeWiden" },
        // { "LeftEyeLower", "/LeftEyeLower" },
        { "LeftEyeBrow", "/LeftEyeBrow" },
        { "RightEyeX", "/RightEyeX" },
        { "RightEyeY", "/RightEyeY" },
        { "RightEyeLid", "/RightEyeLid" },
        { "RightEyeWiden", "/RightEyeWiden" },
        // { "RightEyeLower", "/RightEyeLower" },
        { "RightEyeBrow", "/RightEyeBrow" },
        { "CheekPuffLeft", "/cheekPuffLeft" },
        { "CheekPuffRight", "/cheekPuffRight" },
        { "CheekSuckLeft", "/cheekSuckLeft" },
        { "CheekSuckRight", "/cheekSuckRight" },
        { "JawOpen", "/jawOpen" },
        { "JawForward", "/jawForward" },
        { "JawLeft", "/jawLeft" },
        { "JawRight", "/jawRight" },
        { "NoseSneerLeft", "/noseSneerLeft" },
        { "NoseSneerRight", "/noseSneerRight" },
        { "MouthFunnel", "/mouthFunnel" },
        { "MouthPucker", "/mouthPucker" },
        { "MouthLeft", "/mouthLeft" },
        { "MouthRight", "/mouthRight" },
        { "MouthRollUpper", "/mouthRollUpper" },
        { "MouthRollLower", "/mouthRollLower" },
        { "MouthShrugUpper", "/mouthShrugUpper" },
        { "MouthShrugLower", "/mouthShrugLower" },
        { "MouthClose", "/mouthClose" },
        { "MouthSmileLeft", "/mouthSmileLeft" },
        { "MouthSmileRight", "/mouthSmileRight" },
        { "MouthFrownLeft", "/mouthFrownLeft" },
        { "MouthFrownRight", "/mouthFrownRight" },
        { "MouthDimpleLeft", "/mouthDimpleLeft" },
        { "MouthDimpleRight", "/mouthDimpleRight" },
        { "MouthUpperUpLeft", "/mouthUpperUpLeft" },
        { "MouthUpperUpRight", "/mouthUpperUpRight" },
        { "MouthLowerDownLeft", "/mouthLowerDownLeft" },
        { "MouthLowerDownRight", "/mouthLowerDownRight" },
        { "MouthPressLeft", "/mouthPressLeft" },
        { "MouthPressRight", "/mouthPressRight" },
        { "MouthStretchLeft", "/mouthStretchLeft" },
        { "MouthStretchRight", "/mouthStretchRight" },
        { "TongueOut", "/tongueOut" },
        { "TongueUp", "/tongueUp" },
        { "TongueDown", "/tongueDown" },
        { "TongueLeft", "/tongueLeft" },
        { "TongueRight", "/tongueRight" },
        { "TongueRoll", "/tongueRoll" },
        { "TongueBendDown", "/tongueBendDown" },
        { "TongueCurlUp", "/tongueCurlUp" },
        { "TongueSquish", "/tongueSquish" },
        { "TongueFlat", "/tongueFlat" },
        { "TongueTwistLeft", "/tongueTwistLeft" },
        { "TongueTwistRight", "/tongueTwistRight" }
    };

    // Face expression names ordered by inference output index (matches ParameterSenderService.FaceExpressionMap)
    private static readonly string[] FaceExpressionNames =
    [
        "CheekPuffLeft", "CheekPuffRight", "CheekSuckLeft", "CheekSuckRight",
        "JawOpen", "JawForward", "JawLeft", "JawRight",
        "NoseSneerLeft", "NoseSneerRight",
        "MouthFunnel", "MouthPucker", "MouthLeft", "MouthRight",
        "MouthRollUpper", "MouthRollLower", "MouthShrugUpper", "MouthShrugLower",
        "MouthClose", "MouthSmileLeft", "MouthSmileRight",
        "MouthFrownLeft", "MouthFrownRight",
        "MouthDimpleLeft", "MouthDimpleRight",
        "MouthUpperUpLeft", "MouthUpperUpRight",
        "MouthLowerDownLeft", "MouthLowerDownRight",
        "MouthPressLeft", "MouthPressRight",
        "MouthStretchLeft", "MouthStretchRight",
        "TongueOut", "TongueUp", "TongueDown", "TongueLeft", "TongueRight",
        "TongueRoll", "TongueBendDown", "TongueCurlUp", "TongueSquish",
        "TongueFlat", "TongueTwistLeft", "TongueTwistRight"
    ];

    // Eye lid expression names and their indices in the eye output array
    private static readonly (string Name, int Index)[] EyeLidExpressions =
    [
        ("LeftEyeLid", 2),
        ("RightEyeLid", 5)
    ];

    private readonly ConcurrentDictionary<string, CalibrationParameter> _expressionSettings = new();

    private readonly ILocalSettingsService _localSettingsService;
    private readonly ProcessingLoopService _processingLoopService;

    public bool AutoCalibrationEnabled { get; set; }

    public event Action? AutoCalibrationReset;

    public CalibrationService(ILocalSettingsService localSettingsService, ProcessingLoopService processingLoopService)
    {
        _localSettingsService = localSettingsService;
        _processingLoopService = processingLoopService;

        _processingLoopService.ExpressionChangeEvent += OnExpressionChanged;

        Load();
    }

    private void OnExpressionChanged(ProcessingLoopService.Expressions expressions)
    {
        if (!AutoCalibrationEnabled)
            return;

        // Auto-calibrate face expressions (indexed same as ParameterSenderService.FaceExpressionMap)
        if (expressions.FaceExpression != null)
        {
            for (var i = 0; i < FaceExpressionNames.Length && i < expressions.FaceExpression.Length; i++)
            {
                var name = FaceExpressionNames[i];
                var rawValue = expressions.FaceExpression[i];
                if (_expressionSettings.TryGetValue(name, out var param))
                {
                    if (rawValue > param.Upper) param.Upper = rawValue;
                    if (rawValue < param.Lower) param.Lower = rawValue;
                }
            }
        }

        // Auto-calibrate eye lid expressions
        if (expressions.EyeExpression != null)
        {
            foreach (var (name, index) in EyeLidExpressions)
            {
                if (index >= expressions.EyeExpression.Length)
                    continue;

                var rawValue = expressions.EyeExpression[index];
                if (_expressionSettings.TryGetValue(name, out var param))
                {
                    if (rawValue > param.Upper) param.Upper = rawValue;
                    if (rawValue < param.Lower) param.Lower = rawValue;
                }
            }
        }

        SaveAsync();
    }

    public void SetExpression(string expression, float value)
    {
        if (string.IsNullOrEmpty(expression))
            return;

        if (!expression.EndsWith("Lower") && !expression.EndsWith("Upper")) return;

        var isUpper = expression.EndsWith("Upper");
        var parameterName = expression[..^5]; // Remove "Upper"/"Lower", both 5 letters in size :3

        _expressionSettings.TryGetValue(parameterName, out var currentSettings);

        var lower = isUpper ? currentSettings!.Lower : value;
        var upper = isUpper ? value : currentSettings!.Upper;
        var min = currentSettings!.Min;
        var max = currentSettings.Max;

        var param = new CalibrationParameter(lower, upper, min, max);
        _expressionSettings[parameterName] = param;
        SaveAsync();
    }

    public CalibrationParameter GetExpressionSettings(string parameterName)
    {
        return _expressionSettings.TryGetValue(parameterName, out var settings) ?
            settings :
            new CalibrationParameter();
    }

    public float GetExpressionSetting(string expression)
    {
        if (!expression.EndsWith("Lower") && !expression.EndsWith("Upper")) return 0;

        var isUpper = expression.EndsWith("Upper");
        var parameterName = expression[..^5]; // Remove "Upper"/"Lower", both 5 letters in size :3

        _expressionSettings.TryGetValue(parameterName, out var currentSettings);

        if (currentSettings == null)
            return 0;

        return isUpper ? currentSettings.Upper : currentSettings.Lower;
    }

    private void SaveAsync()
    {
        _localSettingsService.SaveSetting("CalibrationParams", _expressionSettings);
    }

    private void Load()
    {
        var parameters = _localSettingsService.ReadSetting<ConcurrentDictionary<string, CalibrationParameter>?>("CalibrationParams");
        _expressionSettings.Clear();
        if (parameters == null)
        {
            foreach (var parameterName in _eyeExpressionMap)
            {
                _expressionSettings[parameterName.Key] = new CalibrationParameter(-1, 1f, -1f, 1f);
            }

            foreach (var parameterName in _faceExpressionMap)
            {
                _expressionSettings[parameterName.Key] = new CalibrationParameter(0, 1f, 0f, 1f);
            }
        }
        else
        {
            var eyeParameterNames = _eyeExpressionMap.Keys;
            foreach (var parameterName in eyeParameterNames)
            {
                var param = parameters.GetValueOrDefault(parameterName);
                _expressionSettings[parameterName] = param ?? new CalibrationParameter(-1f, 1f, -1f, 1f);
            }
            var faceParameterNames = _faceExpressionMap.Keys;
            foreach (var parameterName in faceParameterNames)
            {
                var param = parameters.GetValueOrDefault(parameterName);
                _expressionSettings[parameterName] = param ?? new CalibrationParameter(0f, 1f, 0f, 1f);
            }
        }
    }

    public void ResetValues()
    {
        foreach (var parameter in _expressionSettings.Values)
        {
            parameter.Lower = parameter.Min;
            parameter.Upper = parameter.Max;
        }
        SaveAsync();
    }

    public void ResetMinimums()
    {
        foreach (var parameter in _expressionSettings.Values)
        {
            parameter.Lower = parameter.Min;
        }
        SaveAsync();
    }

    public void ResetMaximums()
    {
        foreach (var parameter in _expressionSettings.Values)
        {
            parameter.Upper = parameter.Max;
        }
        SaveAsync();
    }

    public void ResetAutoCalibration()
    {
        // Reset Lower/Upper to seed values for all face expressions and eye lids
        foreach (var name in FaceExpressionNames)
        {
            if (_expressionSettings.TryGetValue(name, out var param))
            {
                param.Lower = AutoCalSeed;
                param.Upper = AutoCalSeed;
            }
        }

        foreach (var (name, _) in EyeLidExpressions)
        {
            if (_expressionSettings.TryGetValue(name, out var param))
            {
                param.Lower = AutoCalSeed;
                param.Upper = AutoCalSeed;
            }
        }

        SaveAsync();
        AutoCalibrationReset?.Invoke();
    }
}
