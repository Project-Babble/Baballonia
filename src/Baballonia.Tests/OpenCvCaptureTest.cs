using System;
using System.Threading.Tasks;
using Baballonia.OpenCVCapture;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Baballonia.Tests;

[TestClass]
[TestSubject(typeof(OpenCvCapture))]
public class OpenCvCaptureTest
{

    [TestMethod]
    public async Task OpencvCaptureStartSuccess()
    {
        LoggerFactory factory = new LoggerFactory();
        OpenCvCapture capture = new OpenCvCapture("1", factory.CreateLogger<OpenCvCapture>());
        try
        {
            var res = await capture.StartCapture();
            Assert.IsTrue(res);
        }
        catch(Exception ex)
        {
            Assert.Fail($"Expected no exception, but got: {ex.Message}");
        }
    }
    [TestMethod]
    public async Task OpencvCaptureStartTwoSameSuccess()
    {
        LoggerFactory factory = new LoggerFactory();
        OpenCvCapture capture1 = new OpenCvCapture("1", factory.CreateLogger<OpenCvCapture>());
        OpenCvCapture capture2 = new OpenCvCapture("1", factory.CreateLogger<OpenCvCapture>());
        try
        {
            var res1 = await capture1.StartCapture();
            var res2 = await capture2.StartCapture();
            Assert.IsTrue(res1);
            Assert.IsTrue(res2);
        }
        catch(Exception ex)
        {
            Assert.Fail($"Expected no exception, but got: {ex.Message}");
        }
    }
}
