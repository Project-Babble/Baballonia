using Baballonia.Desktop.Calibration;
using Baballonia.Services.events;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenCvSharp;
using System;

namespace Baballonia.Tests.Calibration;

[TestClass]
public class AsymmetricLidCaptureTests
{
    [DataTestMethod]
    [DataRow(0f, 1f)]
    [DataRow(1f, 0f)]
    public void AddFrame_PreservesIndependentLidLabels(float leftLid, float rightLid)
    {
        var step = new BaseEyeCaptureStep(
            Mock.Of<IEyePipelineEventBus>(),
            "wink",
            flags: 0,
            time: TimeSpan.FromSeconds(1),
            leftLid: leftLid,
            rightLid: rightLid);

        using var leftImage = new Mat(8, 8, MatType.CV_8UC1, Scalar.Black);
        using var rightImage = new Mat(8, 8, MatType.CV_8UC1, Scalar.Black);

        var frame = step.AddFrame([leftImage, rightImage]);

        Assert.AreEqual(leftLid, frame.Header.RoutineLeftLid);
        Assert.AreEqual(rightLid, frame.Header.RoutineRightLid);
    }
}
