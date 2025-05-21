using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baballonia.Helpers;
using Baballonia.Models;
using Baballonia.Services.Inference;
using Baballonia.ViewModels.SplitViewPane;
using Microsoft.Extensions.Hosting;
using OscCore;

namespace Baballonia.Services;

public class ParameterSenderService(
    OscSendService sendService,
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
                if (_leftCameraController != null)  ProcessExpressionData(_leftCameraController.ArExpressions, faceCalibrationViewModel.GetLeftEyeCalibrationValues(), -1f, 1f);
                if (_rightCameraController != null) ProcessExpressionData( _rightCameraController.ArExpressions, faceCalibrationViewModel.GetRightEyeCalibrationValues(), -1f, 1f);
                if (_faceCameraController != null) ProcessExpressionData(_faceCameraController.ArExpressions, faceCalibrationViewModel.GetFaceCalibrationValues(), 0f, 1f);

                await SendAndClearQueue(cancellationToken);
                await Task.Delay(10, cancellationToken);
            }
            catch (Exception)
            {
                // ignore!
            }
        }
    }

    private void ProcessExpressionData(float[] expressions, Dictionary<string, (float Lower, float Upper)> calibrationItems, float min = 0f, float max = 1f)
    {
        if (expressions is null) return;
        if (expressions.Length == 0) return;

        foreach (var (remappedExpression, weight) in calibrationItems.Zip(expressions))
        {
            var msg = new OscMessage(remappedExpression.Key!, weight.Remap(min, max, remappedExpression.Value.Lower, remappedExpression.Value.Upper));
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
