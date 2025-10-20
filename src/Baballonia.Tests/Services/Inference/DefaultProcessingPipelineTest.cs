using System;
using Baballonia.Services;
using Baballonia.Services.Inference;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenCvSharp;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Baballonia.Tests.Services.Inference;

[TestClass]
[TestSubject(typeof(DefaultProcessingPipeline))]
public class EyeProcessingPipelineTest
{
    private DefaultProcessingPipeline defaultProcessingPipeline;

    [TestInitialize]
    public void Initialize()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole(); // Or AddDebug()
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        var eyeInference = new DefaultInferenceRunner(loggerFactory);
        eyeInference.Setup("eyeModel.onnx", false);

        var cameraSource = new Moq.Mock<IVideoSource>();
        cameraSource.Setup(source => source.GetFrame(null)).Returns(new Mat(eyeInference.InputSize.Height, eyeInference.InputSize.Width, MatType.CV_8UC1, new Scalar(150)));

        defaultProcessingPipeline = new DefaultProcessingPipeline();
        defaultProcessingPipeline.VideoSource = cameraSource.Object;
        defaultProcessingPipeline.ImageTransformer = new DualImageTransformer();
        defaultProcessingPipeline.ImageConverter = new MatToFloatTensorConverter();
        defaultProcessingPipeline.InferenceService = eyeInference;

    }

    [TestMethod]
    public void TestSinglePass()
    {
        try
        {
            for(var i = 0; i < 5; i++)
                defaultProcessingPipeline.RunUpdate();
        }
        catch (Exception ex)
        {
            Assert.Fail("Expected no exception, but got: " + ex);
        }
    }
    [TestMethod]
    public void TestMultiPass()
    {
        try
        {
            for(var i = 0; i < 50; i++)
                defaultProcessingPipeline.RunUpdate();
        }
        catch (Exception ex)
        {
            Assert.Fail("Expected no exception, but got: " + ex);
        }
    }

}
