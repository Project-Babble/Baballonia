using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Services.Calibration;

namespace Baballonia.Services;

public class CalibrationService : ICalibrationService
{
    // Expression parameter names (only eyelids - gaze goes directly to OSC)
    private readonly Dictionary<string, string> _eyeExpressionMap = new()
    {
        { "LeftEyeLid", "/LeftEyeLid" },
        { "RightEyeLid", "/RightEyeLid" },
    };

    private readonly Dictionary<string, string> _faceExpressionMap = new()
    {
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

    private readonly ConcurrentDictionary<string, CalibrationParameter> _expressionSettings = new();

    private readonly ILocalSettingsService _localSettingsService;

    private readonly Task _isInitializedTask;
    public CalibrationService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;

        _isInitializedTask = LoadAsync();
    }

    public async Task SetExpression(string expression, float value)
    {
        await _isInitializedTask;

        if (string.IsNullOrEmpty(expression))
            return;

        if (!expression.EndsWith("Lower") && !expression.EndsWith("Upper")) return;

        var isUpper = expression.EndsWith("Upper");
        var parameterName = expression[..^5]; // Remove "Upper"/"Lower", both 5 letters in size :3

        _expressionSettings.TryGetValue(parameterName, out var currentSettings);

        if (currentSettings == null)
        {
            // Create default settings if parameter doesn't exist
            currentSettings = new CalibrationParameter(0f, 1f, 0f, 1f);
        }

        var lower = isUpper ? currentSettings.Lower : value;
        var upper = isUpper ? value : currentSettings.Upper;
        var min = currentSettings.Min;
        var max = currentSettings.Max;

        var param = new CalibrationParameter(lower, upper, min, max);
        _expressionSettings[parameterName] = param;
        await SaveAsync();
    }

    public CalibrationParameter GetExpressionSettings(string parameterName)
    {
        return _expressionSettings.TryGetValue(parameterName, out var settings) ?
            settings :
            new CalibrationParameter();
    }

    public async Task<float> GetExpressionSetting(string expression)
    {
        await _isInitializedTask;

        if (!expression.EndsWith("Lower") && !expression.EndsWith("Upper")) return 0;

        var isUpper = expression.EndsWith("Upper");
        var parameterName = expression[..^5]; // Remove "Upper"/"Lower", both 5 letters in size :3

        _expressionSettings.TryGetValue(parameterName, out var currentSettings);

        if (currentSettings == null)
            return 0;

        return isUpper ? currentSettings.Upper : currentSettings.Lower;
    }

    private async Task SaveAsync()
    {
        await _isInitializedTask;
        await _localSettingsService.SaveSettingAsync("CalibrationParams", _expressionSettings);
    }

    private async Task LoadAsync()
    {
        var parameters = await _localSettingsService.ReadSettingAsync<ConcurrentDictionary<string, CalibrationParameter>?>("CalibrationParams");
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

    public async Task ResetValues()
    {
        await _isInitializedTask;

        foreach (var parameter in _expressionSettings.Values)
        {
            parameter.Lower = parameter.Min;
            parameter.Upper = parameter.Max;
        }
        await SaveAsync();
    }

    public async Task ResetMinimums()
    {
        await _isInitializedTask;
        foreach (var parameter in _expressionSettings.Values)
        {
            parameter.Lower = parameter.Min;
        }
        await SaveAsync();
    }

    public async Task ResetMaximums()
    {
        await _isInitializedTask;
        foreach (var parameter in _expressionSettings.Values)
        {
            parameter.Upper = parameter.Max;
        }
        await SaveAsync();
    }
}
