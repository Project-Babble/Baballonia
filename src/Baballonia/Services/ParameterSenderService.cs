using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaMiaDev.Helpers;
using AvaloniaMiaDev.Models;
using AvaloniaMiaDev.Services.Inference;
using AvaloniaMiaDev.ViewModels.SplitViewPane;
using Microsoft.Extensions.Hosting;
using OscCore;

namespace AvaloniaMiaDev.Services;

public class ParameterSenderService(
    OscSendService sendService,
    EyeCalibrationViewModel eyeCalibrationViewModel,
    FaceCalibrationViewModel faceCalibrationViewModel) : BackgroundService
{
    private readonly Queue<OscMessage> _sendQueue = new();

    public void Enqueue(OscMessage message) => _sendQueue.Enqueue(message);
    public void Clear() => _sendQueue.Clear();

    // Methods to register camera controllers from HomePageView

    // Camera controller references
    private CameraController _leftCameraController;
    private CameraController _rightCameraController;
    private CameraController _faceCameraController;
    public void RegisterLeftCameraController(CameraController controller) => _leftCameraController = controller;
    public void RegisterRightCameraController(CameraController controller) => _rightCameraController = controller;
    public void RegisterFaceCameraController(CameraController controller) => _faceCameraController = controller;

    protected async override Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_leftCameraController != null)  ProcessExpressionData(_leftCameraController.ArExpressions, eyeCalibrationViewModel.LeftEyeCalibrationItems);
                if (_rightCameraController != null) ProcessExpressionData( _rightCameraController.ArExpressions, eyeCalibrationViewModel.RightEyeCalibrationItems);
                if (_faceCameraController != null) ProcessExpressionData(_faceCameraController.ArExpressions, faceCalibrationViewModel.GetCalibrationValues());

                await SendAndClearQueue(cancellationToken);
                await Task.Delay(10, cancellationToken);
            }
            catch (Exception)
            {
                // ignore!
            }
        }
    }

    private void ProcessExpressionData(float[] expressions, Dictionary<string, (float Lower, float Upper)> calibrationItems)
    {
        if (expressions is null) return;
        if (expressions.Length == 0) return;

        foreach (var (remappedExpression, weight) in calibrationItems.Zip(expressions))
        {
            var msg = new OscMessage(remappedExpression.Key!, weight.Remap(0, 1, remappedExpression.Value.Lower, remappedExpression.Value.Upper));
            _sendQueue.Enqueue(msg);
        }
    }

    private void ProcessExpressionData(float[] expressions, CalibrationItem[] calibrationItems)
    {
        if (expressions is null) return;
        if (expressions.Length == 0) return;

        foreach (var (remappedExpression, weight) in calibrationItems.Zip(expressions))
        {
            var msg = new OscMessage(remappedExpression.ShapeName!, Math.Clamp(weight, remappedExpression.Min, remappedExpression.Max));
            _sendQueue.Enqueue(msg);
        }
    }

    private async Task SendAndClearQueue(CancellationToken cancellationToken)
    {
        if (_sendQueue.Count == 0)
            return;

        await sendService.Send(_sendQueue.ToArray(), cancellationToken);
        _sendQueue.Clear();
    }
}
