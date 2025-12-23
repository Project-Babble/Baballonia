using System.Linq;
using Baballonia.Factories;
using Baballonia.Models;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Baballonia.Tests.Models;

[TestClass]
[TestSubject(typeof(FirmwareSessionFactory))]
public class FirmwareSessionFactoryTest
{

    [TestMethod]
    public void BoardIntegrationTest()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole(); // Or AddDebug()
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        FirmwareSessionFactory factory = new FirmwareSessionFactory(loggerFactory, new CommandSenderFactory());

        var sessions = factory.TryOpenAllSessions();
        Assert.IsTrue(sessions.Any());
    }
}
