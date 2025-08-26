using System;
using System.IO;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Desktop.Calibration.Aero.Overlay;
using Baballonia.Models;
using Baballonia.Services;
using Baballonia.Services.Inference;
using CommunityToolkit.Mvvm.DependencyInjection;
using Newtonsoft.Json;
using OpenCvSharp;

namespace Baballonia.Desktop.Calibration.Aero;

public partial class AeroOverlayTrainerCombo
{
    private MjpegStreamingService leftStreamService = new();
    private MjpegStreamingService rightStreamService = new();
    private async Task<bool> StartCamerasAsync(VrCalibration calibration)
    {
        var response = await _httpClient.GetStringAsync(new Uri($"{_baseUrl}/start_cameras?left={calibration.LeftEyeMjpegSource}&right={calibration.RightEyeMjpegSource}"));
        var result = JsonConvert.DeserializeObject<ApiResponse>(response);
        return result!.Result == "ok";
    }

    private async Task<bool> StartCalibrationAsync(VrCalibration calibration)
    {
        var url = $"{_baseUrl}/start_calibration?onnx_filename={VrCalibration.ModelName}&routine_id={calibration.CalibrationInstructions}";
        var response = await _httpClient.GetStringAsync(url);
        var result = JsonConvert.DeserializeObject<ApiResponse>(response);
        return result!.Result == "ok";
    }

    private void HandleEyeImageEvent(Mat image)
    {
        int channels = image.Channels();
        if (channels != 2)
            return;

        var images = image.Split();
        leftStreamService.UpdateMjpegFrame(images[0]);
        rightStreamService.UpdateMjpegFrame(images[1]);

    }
    public async Task EyeTrackingCalibrationRequested(string calibrationRoutine)
    {
        var processingLoop = Ioc.Default.GetService<ProcessingLoopService>()!;

        processingLoop.EyesProcessingPipeline.TransformedFrameEvent += HandleEyeImageEvent;

        const int leftPort = 8080;
        const int rightPort = 8081;
        var modelPath = Directory.GetCurrentDirectory();
        var calibration = new VrCalibration
        {
            ModelSavePath = modelPath,
            CalibrationInstructions = calibrationRoutine,
            FOV = 1f,
            LeftEyeMjpegSource = $"http://localhost:{leftPort}/mjpeg",
            RightEyeMjpegSource = $"http://localhost:{rightPort}/mjpeg",
        };

        // Now for the IPC. Spool up our MJPEG streams
        leftStreamService.StartStreaming(leftPort);
        rightStreamService.StartStreaming(rightPort);

        // Tell the calibrator/overlay to accept our streams, then start calibration
        await StartOverlay();
        await StartCamerasAsync(calibration);
        await StartCalibrationAsync(calibration);

        // Wait for the process to exit
        var loop = true;
        while (loop)
        {
            var status = await GetStatusAsync();
            if (status.IsTrained)
            {
                loop = false;
            }

            await Task.Delay(1000);
        }

        // Stop the MJPEG streams, we don't need them anymore
        StopOverlay();

        processingLoop.EyesProcessingPipeline.TransformedFrameEvent -= HandleEyeImageEvent;

        leftStreamService.StopStreaming();
        rightStreamService.StopStreaming();

        // Cleanup any leftover capture.bin files
        DeleteCaptureFiles(modelPath);
    }
}
