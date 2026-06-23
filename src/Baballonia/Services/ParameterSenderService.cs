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

    // Written ~once/sec on the sender loop, read on the inference worker threads — volatile for a
    // well-defined cross-thread read.
    private volatile string _prefix = "";
    private volatile bool _sendNativeVrcEyeTracking;
    private volatile bool _useDfr;
    private readonly ConcurrentQueue<OscMessage> _vrcftQueue = new();
    private readonly ConcurrentQueue<OscMessage> _dfrQueue = new();
    // Reused drain buffer for the (single-threaded) sender loop.
    private readonly List<OscMessage> _sendBuffer = new();

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

        long lastSettingsRead = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // These settings change only on user edits, but ReadSetting deserializes JSON on
                // every call. Refreshing them on each 10 ms tick was ~300 deserializations/sec for
                // nothing; refresh roughly once per second instead.
                var now = Environment.TickCount64;
                if (now - lastSettingsRead >= 1000)
                {
                    lastSettingsRead = now;
                    _prefix = _localSettingsService.ReadSetting<string>("AppSettings_OSCPrefix");
                    _sendNativeVrcEyeTracking = _localSettingsService.ReadSetting<bool>("VRC_UseNativeTracking");
                    _useDfr = _localSettingsService.ReadSetting<bool>("AppSettings_UseDFR");
                }
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
        await DrainAndSend(_vrcftQueue, _vrcftModuleSendService, cancellationToken);
        await DrainAndSend(_dfrQueue, _dfrSendService, cancellationToken);
    }

    /// <summary>
    /// Atomically drains the queue (via TryDequeue) and sends it. The previous ToArray()+Clear()
    /// was not atomic — anything enqueued between the snapshot and the Clear() was silently dropped.
    /// </summary>
    private async Task DrainAndSend(ConcurrentQueue<OscMessage> queue, OscSendService sender,
        CancellationToken cancellationToken)
    {
        if (queue.IsEmpty)
            return;

        _sendBuffer.Clear();
        while (queue.TryDequeue(out var message))
            _sendBuffer.Add(message);

        if (_sendBuffer.Count > 0)
            await sender.Send(_sendBuffer.ToArray(), cancellationToken);
    }
}
