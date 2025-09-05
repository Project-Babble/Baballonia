using System;
using System.Collections.Generic;
using System.IO;
using Baballonia.Contracts;
using Baballonia.Helpers;
using Baballonia.Models;
using Baballonia.Services;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Baballonia.Tests.FirmwareTests
{
    class MockCommandSender : ICommandSender
    {
        private List<string> lines;

        public MockCommandSender(List<string> lines)
        {
            this.lines = lines;
        }

        public void Dispose()
        {

        }

        public string ReadLine(TimeSpan timeout)
        {
            string line = lines[0];
            lines.RemoveAt(0);

            return line;
        }

        public void WriteLine(string message)
        {
        }
    }
    [TestClass]
    public class FirmwareServiceTest
    {
        private ILoggerFactory _loggerFactory;
        private ILogger _logger;

        [TestInitialize]
        public void Initialize()
        {
            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddConsole()
                    .SetMinimumLevel(LogLevel.Trace);
            });

            _logger = _loggerFactory.CreateLogger("TEST");
        }


        private FirmwareService CreateService(List<string> mockResponses)
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddConsole()
                    .SetMinimumLevel(LogLevel.Trace);
            });

            var serviceLogger = loggerFactory.CreateLogger<FirmwareService>();
            var logger = loggerFactory.CreateLogger<FirmwareServiceTest>();

            var mockCommandSenderFactory = new Mock<ICommandSenderFactory>();
            mockCommandSenderFactory
                .Setup(s => s.Create(CommandSenderType.Serial, "COM4"))
                .Returns(new MockCommandSender(mockResponses));

            return new FirmwareService(serviceLogger, mockCommandSenderFactory.Object);
        }
        [TestMethod]
        public void TestSendCommand()
        {
            List<string> mockLines = new List<string>();
            mockLines.Add("""{"heartbeat":{}}""");
            mockLines.Add("""{"results":        ["{\"result\":\"{\\\"status\\\":\\\"connected\\\",\\\"networks_configured\\\":1,\\\"ip_address\\\":\\\"192.168.0.246\\\"}\"}"]} """);

            var firmwareService = CreateService(mockLines);

            var session = firmwareService.StartSession(CommandSenderType.Serial, "COM4");
            session.WaitForHeartbeat();

            var results = session.SendCommand(new FirmwareRequests.GetWifiStatusRequest(), TimeSpan.FromSeconds(10));
            Assert.AreEqual("192.168.0.246", results.IpAddress);
        }


        [TestMethod]
        public void TestSendBatchCommand()
        {
            // List<string> mockLines = new List<string>();
            // mockLines.Add("""{"heartbeat":{}}""");
            // mockLines.Add("""
            //         {"results":[
            //         {"command_name":"pause", "status": "SUCCESS" },
            //         {"command_name":"get_wifi_status", "status": "SUCCESS" }
            //     ]}
            //     """);
            //
            // var firmwareService = CreateService(mockLines);
            // var session = firmwareService.StartSession(CommandSenderType.Serial, "COM4");
            // session.WaitForHeartbeat();
            //
            // session.SendCommand(new FirmwareRequests.SetPausedRequest(true));
            // var commandResult = session.SendCommand(new FirmwareRequests.GetWifiStatusRequest());
            // Assert.AreEqual("");
        }

        [TestMethod]
        public void TestSendGeneric()
        {
            List<string> mockLines = new List<string>();
            mockLines.Add("""{"heartbeat":{}}""");
            mockLines.Add("""
                              {"results":[
                              "{\"command_name\":\"pause\", \"status\":\"SUCCESS\"}"
                          ]}
                          """);

            var firmwareService = CreateService(mockLines);
            var session = firmwareService.StartSession(CommandSenderType.Serial, "COM4");
            session.WaitForHeartbeat();

            var commandResult = session.SendCommand(new FirmwareRequests.SetPausedRequest(true), TimeSpan.FromSeconds(10));
            Assert.AreEqual("""
                            {"command_name":"pause", "status":"SUCCESS"}
                            """, commandResult);
        }


    }
}
