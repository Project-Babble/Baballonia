using System;
using System.Linq;
using Baballonia.Helpers;
using Baballonia.Models;
using Baballonia.Services;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Baballonia.Tests.FirmwareTests
{
    // Here we'll put integration testing with a real microcontroller
    [TestClass]
    public class FirmwareIntegrationTest
    {
        private static readonly string PORT = "COM4";
        private static readonly string WIFI_SSID = "ASUS_50";
        private static readonly string WIFI_PWD = "172839456";

        private ILogger _logger;

        [TestInitialize]
        public void Initialize()
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddConsole()
                    .SetMinimumLevel(LogLevel.Debug);
            });

            _logger = loggerFactory.CreateLogger("TEST");
        }

        [TestMethod]
        public void TestBoard()
        {
            var session = new FirmwareSession(new SerialCommandSender(PORT), _logger);
            Assert.IsNotNull(session.WaitForHeartbeat());
        }

        [TestMethod]
        public void FindAndConnectWifiSuccess()
        {
            var session = new FirmwareSession(new SerialCommandSender(PORT), _logger);

            session.WaitForHeartbeat();
            session.SendCommand(new FirmwareRequests.SetPausedRequest(true), TimeSpan.FromSeconds(30));
            var networks = session.SendCommand(new FirmwareRequests.ScanWifiRequest(), TimeSpan.FromSeconds(30));
            Assert.IsNotNull(networks);

            var find = networks.Networks.Find(network => network.Ssid == WIFI_SSID);
            Assert.IsNotNull(find);

            session.SendCommand(new FirmwareRequests.SetWifiRequest(WIFI_SSID, WIFI_PWD), TimeSpan.FromSeconds(30));

            var connectionres = session.SendCommand(new FirmwareRequests.ConnectWifiRequest(), TimeSpan.FromSeconds(30));
            Assert.IsNotNull(connectionres);

            var wifistatus = session.SendCommand(new FirmwareRequests.GetWifiStatusRequest(), TimeSpan.FromSeconds(30));
            Assert.IsNotNull(wifistatus);
            Assert.AreEqual("connected", wifistatus.Status);

            session.SendCommand(new FirmwareRequests.SetPausedRequest(false), TimeSpan.FromSeconds(30));

            session.Dispose();
        }

        [TestMethod]
        public void FindAndConnectWifiFail()
        {
            var session = new FirmwareSession(new SerialCommandSender(PORT), _logger);

            session.WaitForHeartbeat();
            session.SendCommand(new FirmwareRequests.SetPausedRequest(true), TimeSpan.FromSeconds(30));
            var networks = session.SendCommand(new FirmwareRequests.ScanWifiRequest(), TimeSpan.FromSeconds(30));
            Assert.IsNotNull(networks);

            var find = networks.Networks.Find(network => network.Ssid == WIFI_SSID);
            Assert.IsNotNull(find);

            session.SendCommand(new FirmwareRequests.SetWifiRequest("", WIFI_PWD), TimeSpan.FromSeconds(30));

            var connectionres = session.SendCommand(new FirmwareRequests.ConnectWifiRequest(), TimeSpan.FromSeconds(30));
            Assert.IsNotNull(connectionres);

            var wifistatus = session.SendCommand(new FirmwareRequests.GetWifiStatusRequest(), TimeSpan.FromSeconds(30));
            Assert.IsNotNull(wifistatus);
            Assert.AreEqual("error", wifistatus.Status);

            session.SendCommand(new FirmwareRequests.SetPausedRequest(false), TimeSpan.FromSeconds(30));

            session.Dispose();
        }
    }
}
