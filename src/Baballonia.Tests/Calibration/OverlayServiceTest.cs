using System;
using System.Threading;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Desktop.Calibration;
using Baballonia.Desktop.Calibration.Aero;
using Baballonia.Desktop.Trainer;
using Baballonia.Helpers;
using Baballonia.Services;
using Baballonia.Services.events;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenCvSharp;
using OverlaySDK.Packets;

namespace Baballonia.Tests.Calibration;

[TestClass]
[TestSubject(typeof(OverlayTrainerService))]
public class OverlayTrainerServiceTest
{
    [TestMethod]
    public async Task Test()
    {
        // ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().AddDebug());
        //
        // EyePipelineEventBus eyePipelineEventBus = new EyePipelineEventBus();
        // CancellationTokenSource tokenSource = new CancellationTokenSource();
        // Task.Run(() =>
        // {
        //     while (!tokenSource.Token.IsCancellationRequested)
        //     {
        //         Mat frame1 = new Mat(256, 256, MatType.CV_8UC1);
        //         Mat frame2 = new Mat(256, 256, MatType.CV_8UC1);
        //         Mat combiner = new Mat();
        //
        //         Cv2.Merge([frame1, frame2], combiner);
        //
        //         eyePipelineEventBus.Publish(new EyePipelineEvents.NewTransformedFrameEvent(combiner));
        //         Thread.Sleep(22);
        //     }
        // });
        //
        // var mockTrainer = new Mock<ITrainerService>();
        // var progressEvent = new Action<TrainerProgressReportPacket>((p) => { });
        // mockTrainer.SetupAdd(m => m.OnProgress += It.IsAny<Action<TrainerProgressReportPacket>>())
        //     .Callback<Action<TrainerProgressReportPacket>>(h => progressEvent += h);
        // mockTrainer.SetupRemove(m => m.OnProgress -= It.IsAny<Action<TrainerProgressReportPacket>>())
        //     .Callback<Action<TrainerProgressReportPacket>>(h => progressEvent -= h);
        //
        // mockTrainer.Setup(m => m.RunTraining(It.IsAny<string>(), It.IsAny<string>()))
        //     .Callback(() =>
        //     {
        //         progressEvent?.Invoke(new TrainerProgressReportPacket("Batch", 1, 100, 0.123));
        //         Thread.Sleep(2000);
        //         progressEvent?.Invoke(new TrainerProgressReportPacket("Epoch", 1, 10, 0.045));
        //         Thread.Sleep(2000);
        //         progressEvent?.Invoke(new TrainerProgressReportPacket("Epoch", 10, 10, 0.005));
        //     });
        //
        // mockTrainer.Setup(m => m.WaitAsync())
        //     .Returns(Task.CompletedTask);
        //
        // var mockOverlayProgram = new Mock<IOverlayProgram>();
        // mockOverlayProgram.Setup(program => program.CanStart()).Returns(true);
        // mockOverlayProgram.Setup(program => program.WaitForExitAsync()).Returns(Task.CompletedTask);
        //
        // OverlayTrainerService overlayTrainerService =
        //     new OverlayTrainerService(loggerFactory.CreateLogger<OverlayTrainerService>(), eyePipelineEventBus, mockTrainer.Object, mockOverlayProgram.Object);
        //
        // await overlayTrainerService.EyeTrackingCalibrationRequested(CalibrationRoutine.Map["BasicCalibration"]);
        //
        // tokenSource.Cancel();
        //
        //
        // mockTrainer.Verify(service => service.RunTraining(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }
}
