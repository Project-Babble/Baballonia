using Baballonia.Contracts;
using Baballonia.Helpers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OscCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Baballonia.Services;

public class ParameterSenderService : BackgroundService
{
    private readonly VrcftModuleSendService _vrcftModuleSendService;
    private readonly DfrSendService _dfrSendService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ICalibrationService _calibrationService;
    private readonly ILogger<ParameterSenderService> _logger;

    private string _prefix = "";
    private bool _sendNativeVrcEyeTracking;
    private bool _useDfr;
    private readonly ConcurrentQueue<OscMessage> _vrcftQueue = new();
    private readonly ConcurrentQueue<OscMessage> _dfrQueue = new();

    public ParameterSenderService(
        VrcftModuleSendService vrcftModuleSendService,
        DfrSendService dfrSendService,
        ILocalSettingsService localSettingsService,
        ICalibrationService calibrationService,
        ProcessingLoopService processingLoopService,
        ILogger<ParameterSenderService> logger)
    {
        this._vrcftModuleSendService = vrcftModuleSendService;
        this._dfrSendService = dfrSendService;
        this._localSettingsService = localSettingsService;
        this._calibrationService = calibrationService;
        this._logger = logger;

        processingLoopService.ExpressionChangeEvent += ExpressionUpdateHandler;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting Parameter Sender Service...");
        _logger.LogDebug("OSC parameter mapping initialized");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _prefix = _localSettingsService.ReadSetting<string>("AppSettings_OSCPrefix");
                _sendNativeVrcEyeTracking = _localSettingsService.ReadSetting<bool>("VRC_UseNativeTracking");
                _useDfr = _localSettingsService.ReadSetting<bool>("AppSettings_UseDFR");
                await SendAndClearQueue(cancellationToken);
                await Task.Delay(10, cancellationToken);
            }
            catch (Exception)
            {
                // ignore
            }
        }
    }

    private void ExpressionUpdateHandler(ProcessingLoopService.Expressions expressions)
    {
        if (expressions.EyeExpression != null)
            ProcessEyeExpressionData(expressions.EyeExpression);
        if (expressions.FaceExpression != null)
            ProcessFaceExpressionData(expressions.FaceExpression);
    }

    private void ProcessEyeExpressionData(OrderedFloatMap expressions)
    {
        if (expressions is null) return;

        foreach (var expression in expressions)
        {
            float weight = expression.Value;
            var settings = _calibrationService.GetExpressionSettings(expression.Key);

            var msg = new OscMessage(_prefix + expression.Key,
                weight.Remap(settings.Lower, settings.Upper, settings.Min, settings.Max));
            _vrcftQueue.Enqueue(msg);
        }

        if (_useDfr)
            ProcessNativeVrcEyeTracking(expressions, _dfrQueue);

        if (_sendNativeVrcEyeTracking)
            ProcessNativeVrcEyeTracking(expressions, _vrcftQueue);
    }

    private void ProcessNativeVrcEyeTracking(OrderedFloatMap expressions, ConcurrentQueue<OscMessage> queue)
    {
        var leftEyeX = expressions["/leftEyePitch"];
        var leftEyeY = expressions["/leftEyeYaw"];
        var leftEyeLid = expressions["/leftEyeLid"];
        var rightEyeX = expressions["/rightEyePitch"];
        var rightEyeY = expressions["/rightEyeYaw"];
        var rightEyeLid = expressions["/rightEyeLid"];

        var leftEyeLidSettings = _calibrationService.GetExpressionSettings("LeftEyeLid");
        var rightEyeLidSettings = _calibrationService.GetExpressionSettings("RightEyeLid");
        var weightedLeftEyeLid = leftEyeLid.Remap(leftEyeLidSettings.Lower, leftEyeLidSettings.Upper, leftEyeLidSettings.Min, leftEyeLidSettings.Max);
        var weightedRightEyeLid = rightEyeLid.Remap(rightEyeLidSettings.Lower, rightEyeLidSettings.Upper, rightEyeLidSettings.Min, rightEyeLidSettings.Max);
        var averageLid = (weightedLeftEyeLid + weightedRightEyeLid) / 2f;
        queue.Enqueue(new OscMessage("/tracking/eye/EyesClosedAmount", 1f - Math.Clamp(averageLid, 0f, 1f)));

        // Convert normalized eye positions to angles
        const float maxEyeAngle = 45f;
        leftEyeX *= maxEyeAngle;
        leftEyeY *= -maxEyeAngle; // Negative because Y is inverted (up is negative pitch)
        rightEyeX *= maxEyeAngle;
        rightEyeY *= -maxEyeAngle; // Negative because Y is inverted (up is negative pitch)
        queue.Enqueue(new OscMessage("/tracking/eye/LeftRightPitchYaw", leftEyeY, rightEyeX, rightEyeY, leftEyeX));
    }

    private void ProcessFaceExpressionData(OrderedFloatMap expressions)
    {
        if (expressions == null) return;

        foreach (var expression in expressions)
        {
            float weight = expression.Value;
            var settings = _calibrationService.GetExpressionSettings(expression.Key);

            var msg = new OscMessage(_prefix + expression.Key,
                Math.Clamp(
                    weight.Remap(settings.Lower, settings.Upper, settings.Min, settings.Max),
                    settings.Min,
                    settings.Max));
            _vrcftQueue.Enqueue(msg);
        }
    }

    private async Task SendAndClearQueue(CancellationToken cancellationToken)
    {
        if (!_vrcftQueue.IsEmpty)
        {
            await _vrcftModuleSendService.Send(_vrcftQueue.ToArray(), cancellationToken);
            _vrcftQueue.Clear();
        }

        if (!_dfrQueue.IsEmpty)
        {
            await _dfrSendService.Send(_dfrQueue.ToArray(), cancellationToken);
            _dfrQueue.Clear();
        }
    }
}
