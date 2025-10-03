using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baballonia.Assets;
using Baballonia.CaptureBin.IO;
using Baballonia.Contracts;
using Baballonia.Services;
using Baballonia.Services.events;
using Baballonia.ViewModels.SplitViewPane;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OverlaySDK;
using OverlaySDK.Packets;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Baballonia.Desktop.Calibration.Aero;

public class AeroOverlayTrainerCombo : IVROverlay
{
    private enum OverlayState
    {
        GazeTutorial,
        Gaze,
        BlinkTutorial,
        Blink,
    }

    private IEyePipelineEventBus _eyePipelineEventBus;
    private ILogger Logger;

    static AeroOverlayTrainerCombo()
    {
        var isWindows = OperatingSystem.IsWindows();
        OverlayPath = Path.Combine(AppContext.BaseDirectory, "Calibration", isWindows ? "Windows" : "Linux", "Overlay");
        Overlay = Path.Combine(OverlayPath, isWindows ? "BabbleCalibration.x86_64.exe" : "BabbleCalibration.x86_64");
    }

    private static string Overlay { get; } = null!;
    private static string OverlayPath { get; } = null!;

    private static HmdPositionalDataPacket _currentPacket = new();
    private static OverlayState _currentState = OverlayState.GazeTutorial;
    private static List<Frame> _currentFrames = new();


    public async Task<(bool, string)> EyeTrackingCalibrationRequested(string calibrationRoutine)
    {
        // Need to pull here, the service provider isn't present until this method is called
        Logger ??= Ioc.Default.GetService<ILogger<HomePageViewModel>>()!;

        _eyePipelineEventBus = Ioc.Default.GetService<IEyePipelineEventBus>()!;
        _eyePipelineEventBus.Subscribe<EyePipelineEvents.NewTransformedFrameEvent>(HandleEyeImageEvent);

        // Now for the IPC. Spool up our MJPEG streams
        //_leftStreamService.StartStreaming(leftPort);
        //_rightStreamService.StartStreaming(rightPort);

        // Tell the calibrator/overlay start...
        var status = await StartOverlay();
        var success = status.success;
        if (!success) return await StopStreamingAndReturn(status.message);

        // Stop streaming, cleanup. No need to report an error state
        await StopStreamingAndReturn(string.Empty);

        // Cleanup any leftover capture.bin files
        //DeleteCaptureFiles(modelPath);
        return await Task.FromResult((success, "TEMP"));
    }

    private static void DeleteCaptureFiles(string directoryPath)
    {
        // Validate directory exists
        if (!Directory.Exists(directoryPath))
            return;

        // Get all files matching the capture pattern
        var filesToDelete = Directory.GetFiles(directoryPath, "capture.bin");

        // Delete each file
        foreach (var file in filesToDelete) File.Delete(file);
    }

    private async Task<(bool success, string message)> StartProcess(string program,
        string[]? arguments = null,
        bool waitForExit = false)
    {
        // Make sure the overlay program exists
        if (!File.Exists(program))
        {
            Logger.LogError(Resources.Aero_Overlay_NotFound);
            return (false, Resources.Aero_Overlay_NotFound);
        }

        var processName = Path.GetFileNameWithoutExtension(program);

        // Make sure program isn't already running
        var hitList = Process.GetProcesses().Where(p => p.ProcessName == processName).ToArray();
        if (hitList.Length > 0)
        {
            Logger.LogError(Resources.Aero_Overlay_AlreadyRunning);
            foreach (var p in hitList) p.Kill(true);

            // return (false, Assets.Resources.Aero_Overlay_AlreadyRunning);
        }

        var processList = Process.GetProcesses();
        var steamvr = processList.Any(p => p.ProcessName.ToLower().Contains("vrserver"));
        var monado = processList.Any(p => p.ProcessName.ToLower().Contains("monado"));
        var isWindows = OperatingSystem.IsWindows();

        var launchArgs = "";

        if (!steamvr && !monado)
        {
            Logger.LogError(Resources.Aero_SteamVR_NotRunning);
            return (false, Resources.Aero_SteamVR_NotRunning);
        }

        //TODO: enable OpenXR overlay mode on supported runtimes (monado) whenever OpenXR overlays are supported
        if (isWindows)
        {
            if (steamvr)
                launchArgs = "--use-openvr";
            else if (monado) launchArgs = "--xr-mode on"; //uhhhhh?????
        }
        else
        {
            launchArgs = "--xr-mode on";
        }
        //linux always runs openxr standalone because overlays aren't supported and steamvr overlay segfaults

        var startInfo = new ProcessStartInfo
        {
            FileName = program,
            Arguments = launchArgs
        };

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.Start();

        var logger = new AeroOverlayLogger(Logger);

        var sfactory = new SocketFactory();
        var sock = sfactory.CreateServer("127.0.0.1", 2425);

        var tcp = new EventDrivenTcpClient(sock);
        var client = new EventDrivenJsonClient(tcp);

        var messageDispatcher = new OverlayMessageDispatcher(logger, client);
        var handlerInstance = new OverlayPacketHandler(messageDispatcher);

        handlerInstance.OnPositionData += packet => _currentPacket = packet;

        messageDispatcher.RegisterHandler(handlerInstance);

        messageDispatcher.Dispatch(new RunFixedLenghtRoutinePacket("gazetutorial"));
        _currentState = OverlayState.GazeTutorial;

        Thread.Sleep(1000 * 28);

        messageDispatcher.Dispatch(new RunVariableLenghtRoutinePacket("gaze", TimeSpan.FromSeconds(15)));

        Thread.Sleep(1000);

        _currentState = OverlayState.Gaze;

        Thread.Sleep(1000 * 14);

        messageDispatcher.Dispatch(new RunFixedLenghtRoutinePacket("close"));

        if (waitForExit) await process.WaitForExitAsync();

        _currentState = OverlayState.GazeTutorial;
        _currentPacket = new HmdPositionalDataPacket();
        _currentFrames.Clear();

        return (true, string.Empty);
    }

    private async Task<(bool, string)> StopStreamingAndReturn(string message)
    {
        _eyePipelineEventBus.Unsubscribe<EyePipelineEvents.NewTransformedFrameEvent>(HandleEyeImageEvent);
        return await Task.FromResult((false, message));
    }

    private async Task<(bool success, string message)> StartOverlay(string[]? arguments = null,
        bool waitForExit = false)
    {
        return await StartProcess(Overlay, arguments, waitForExit);
    }

    private void HandleEyeImageEvent(EyePipelineEvents.NewTransformedFrameEvent e)
    {
        if (_currentState is (OverlayState.GazeTutorial or OverlayState.BlinkTutorial)) return;

        var image = e.image;
        var channels = image.Channels();
        if (channels != 2)
            return;

        var images = image.Split();

        const int jpegQuality = 50;

        Cv2.ImEncode(".jpg", images[0], out var bufLeft, [(int)ImwriteFlags.JpegQuality, jpegQuality]);
        Cv2.ImEncode(".jpg", images[1], out var bufRight, [(int)ImwriteFlags.JpegQuality, jpegQuality]);

        switch (_currentState)
        {
            case OverlayState.Gaze:
            {
                _currentFrames.Add(new Frame
                {
                    Header = GenerateHeader() with
                    {
                        RoutineState = CaptureFlags.FLAG_IN_MOVEMENT | CaptureFlags.FLAG_GOOD_DATA,
                        JpegDataLeftLength = (uint)bufLeft.Length,
                        JpegDataRightLength = (uint)bufRight.Length
                    },
                    LeftJpeg = bufLeft, RightJpeg = bufRight
                });
                break;
            }
            case OverlayState.Blink:
            {
                _currentFrames.Add(new Frame
                {
                    Header = GenerateHeader() with
                    {
                        RoutineState = CaptureFlags.FLAG_RESTING | CaptureFlags.FLAG_GOOD_DATA,
                        JpegDataLeftLength = (uint)bufLeft.Length,
                        JpegDataRightLength = (uint)bufRight.Length
                    },
                    LeftJpeg = bufLeft, RightJpeg = bufRight
                });
                break;
            }
        }

        return;

        CaptureFrameHeader GenerateHeader()
        {
            var time = (ulong)DateTimeOffset.Now.ToUnixTimeMilliseconds();
            return new CaptureFrameHeader
            {
                LeftEyePitch = _currentPacket.LeftEyePitch,
                LeftEyeYaw = _currentPacket.LeftEyeYaw,
                RightEyePitch = _currentPacket.RightEyePitch,
                RightEyeYaw = _currentPacket.RightEyeYaw,
                RoutinePitch = _currentPacket.RoutinePitch,
                RoutineYaw = _currentPacket.RoutineYaw,
                RoutineDistance = _currentPacket.RoutineDistance,
                RoutineConvergence = _currentPacket.RoutineConvergence,
                FovAdjustDistance = _currentPacket.FovAdjustDistance,
                Timestamp = time,
                TimestampLeft = time,
                TimestampRight = time,
            };
        }
    }

    public void Dispose()
    {
        //YIPPEE
    }
}
