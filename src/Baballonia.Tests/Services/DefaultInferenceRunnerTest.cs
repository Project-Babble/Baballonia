using System;
using Baballonia.Services;
using Baballonia.Services.Inference;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenCvSharp;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Baballonia.Tests.Services;

[TestClass]
[TestSubject(typeof(DefaultInferenceRunner))]
public class DefaultInferenceRunnerTest
{
    private DefaultInferenceRunner _defaultInference;

    [TestInitialize]
    public void Initialize()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole(); // Or AddDebug()
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        try
        {
            _defaultInference = new DefaultInferenceRunner(loggerFactory);
            _defaultInference.Setup("faceModel.onnx", false);
        }
        catch (Exception ex)
        {
            Assert.Fail("Expected no exception, but got: " + ex);
        }
    }

    [TestMethod]
    public void TestUseGpuNotThrows()
    {
        try
        {
            _defaultInference.Setup("faceModel.onnx", true);
        }
        catch (Exception ex)
        {
            Assert.Fail("Expected no exception, but got: " + ex);
        }
    }

    [TestMethod]
    public void TestSinglePass()
    {
        var image = new Mat(_defaultInference.InputSize.Height, _defaultInference.InputSize.Width,
            MatType.CV_32F, new Scalar(0.5f));
        var conv = new MatToFloatTensorConverter();
        conv.Convert(image, _defaultInference.InputTensor);
        var result = _defaultInference.Run();
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void TestMultiPass()
    {
        for (var i = 0; i < 10; i++)
        {
            var image = new Mat(224, 224, MatType.CV_32F, new Scalar(0.5f));
            var conv = new MatToFloatTensorConverter();
            conv.Convert(image, _defaultInference.InputTensor);
            var result = _defaultInference.Run();
            Assert.IsNotNull(result);
        }
    }
}
