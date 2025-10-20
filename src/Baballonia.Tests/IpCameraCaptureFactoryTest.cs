using Baballonia.Android.Captures;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Baballonia.Tests;

[TestClass]
[TestSubject(typeof(IpCameraCaptureFactory))]
public class IpCameraCaptureFactoryTest
{

    [TestMethod]
    public void CanConnectShouldFail()
    {
        LoggerFactory loggerFactory = new LoggerFactory();
        IpCameraCaptureFactory factory = new(loggerFactory);
        var val = factory.CanConnect("OBS Virtual Camera");

        Assert.IsFalse(val);
    }

}
