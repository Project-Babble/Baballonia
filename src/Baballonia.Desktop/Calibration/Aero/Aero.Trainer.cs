using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Baballonia.Desktop.Calibration.Aero.Overlay;
using Baballonia.Models;
using Baballonia.Services;
using Baballonia.ViewModels.SplitViewPane;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenCvSharp;

namespace Baballonia.Desktop.Calibration.Aero;

public partial class AeroOverlayTrainerCombo
{
    private readonly MjpegStreamingService _leftStreamService = new();
    private readonly MjpegStreamingService _rightStreamService = new();

    public async Task<(bool, string)> EyeTrackingCalibrationRequested(string calibrationRoutine)
    {
        // Need to pull here, the service provider isn't present until this method is called
        Logger ??= Ioc.Default.GetService<ILogger<HomePageViewModel>>()!;

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
        _leftStreamService.StartStreaming(leftPort);
        _rightStreamService.StartStreaming(rightPort);

        // Tell the calibrator/overlay start...
        var success = await StartOverlay();
        if (!success)
        {
            processingLoop.EyesProcessingPipeline.TransformedFrameEvent -= HandleEyeImageEvent;
            _leftStreamService.StopStreaming();
            _rightStreamService.StopStreaming();
            return await Task.FromResult((false, "Failed to start overlay!"));
        }

        // Then connect the camera streams...
        success = await StartCamerasAsync(calibration);
        if (!success)
        {
            processingLoop.EyesProcessingPipeline.TransformedFrameEvent -= HandleEyeImageEvent;
            _leftStreamService.StopStreaming();
            _rightStreamService.StopStreaming();
            return await Task.FromResult((false,  "Failed to start camera streaming!"));
        }

        // If we have a good start on the overlay/streams, then start calibration
        success = await StartCalibrationAsync(calibration);
        if (!success)
        {
            processingLoop.EyesProcessingPipeline.TransformedFrameEvent -= HandleEyeImageEvent;
            _leftStreamService.StopStreaming();
            _rightStreamService.StopStreaming();
            return await Task.FromResult((false, "Failed to start calibration!"));
        }

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
        success = StopOverlay();
        var outputMessage = success ?
            "Eye tracking calibration successful!" :
            "Failed to stop/cleanup overlay!";

        processingLoop.EyesProcessingPipeline.TransformedFrameEvent -= HandleEyeImageEvent;

        _leftStreamService.StopStreaming();
        _rightStreamService.StopStreaming();

        // Cleanup any leftover capture.bin files
        DeleteCaptureFiles(modelPath);
        return await Task.FromResult((success, outputMessage));
    }

    private async Task<bool> StartOverlay(string[]? arguments = null, string[]? blacklistedPrograms = null, bool waitForExit = false)
    {
        return await StartProcess(Overlay, arguments, blacklistedPrograms, waitForExit);
    }

    private bool StopOverlay()
    {
        return StopProcess(Overlay);
    }

    private async Task<bool> StartCamerasAsync(VrCalibration calibration)
    {
        try
        {
            var response = await _httpClient.GetStringAsync(new Uri(
                $"{_baseUrl}/start_cameras?left={calibration.LeftEyeMjpegSource}&right={calibration.RightEyeMjpegSource}"));
            var result = JsonConvert.DeserializeObject<ApiResponse>(response);
            return result!.Result == "ok";
        }
        catch (HttpRequestException e)
        {
            Logger.LogError(e.Message);
            return false;
        }
        catch (Exception e)
        {
            Logger.LogError(e.Message);
            throw;
        }
    }

    private async Task<bool> StartCalibrationAsync(VrCalibration calibration)
    {
        try
        {
            var url = $"{_baseUrl}/start_calibration?onnx_filename={VrCalibration.ModelName}&routine_id={calibration.CalibrationInstructions}";
            var response = await _httpClient.GetStringAsync(url);
            var result = JsonConvert.DeserializeObject<ApiResponse>(response);
            return result!.Result == "ok";
        }
        catch (HttpRequestException e)
        {
            Logger.LogError(e.Message);
            return false;
        }
        catch (Exception e)
        {
            Logger.LogError(e.Message);
            throw;
        }
    }

    private void HandleEyeImageEvent(Mat image)
    {
        int channels = image.Channels();
        if (channels != 2)
            return;

        var images = image.Split();
        _leftStreamService.UpdateMjpegFrame(images[0]);
        _rightStreamService.UpdateMjpegFrame(images[1]);
    }
}
