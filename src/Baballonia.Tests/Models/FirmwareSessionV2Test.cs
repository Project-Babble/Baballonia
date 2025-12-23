using System;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using Baballonia.Contracts;
using Baballonia.Helpers;
using Baballonia.Models;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Baballonia.Tests.Models;

[TestClass]
[TestSubject(typeof(FirmwareSessionV2))]
public class FirmwareSessionV2Test
{
    public string[] FindAvailableSerialPorts()
    {
        // GetPortNames() may return single port multiple times
        // https://stackoverflow.com/questions/33401217/serialport-getportnames-returns-same-port-multiple-times
        return SerialPort.GetPortNames().Distinct().ToArray();
    }

    FirmwareSessionV2? FindAnyCreateSession()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole(); // Or AddDebug()
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        var logger = loggerFactory.CreateLogger("Balls");
        var ports = FindAvailableSerialPorts();
        foreach (var port in ports)
        {
            try
            {
                ICommandSender command = new SerialCommandSender(port);
                FirmwareSessionV2 firmwareSession =
                    new FirmwareSessionV2(command, loggerFactory.CreateLogger<FirmwareSessionV2>());

                var res = firmwareSession.SendCommand(new FirmwareRequests.GetWhoAmIRequest(), TimeSpan.FromSeconds(5));
                if (res.IsSuccess)
                    return firmwareSession;

                firmwareSession.Dispose();
            }
            catch (Exception any)
            {
                logger.LogError(any.Message);
            }
        }

        return null;
    }

    public static void AssertAreEqualIgnoreCase(string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail($"Expected '{expected}' but got '{actual}' (case-insensitive compare)");
        }
    }

    [TestMethod]
    public void GetModeTest()
    {

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole(); // Or AddDebug()
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        ICommandSender commandSender = new SerialCommandSender("COM3");
        FirmwareSessionV2 sessionV2 = new FirmwareSessionV2(commandSender, loggerFactory.CreateLogger<FirmwareSessionV2>());

        var res = sessionV2.SendCommand(new FirmwareRequests.GetDeviceModeRequestV2(), TimeSpan.MaxValue);

        Assert.IsTrue(res.IsSuccess);
        Assert.IsTrue(res.Value!.mode == "UVC");
    }
    [TestMethod]
    public void AllCommandsIntegrationTest()
    {
        var sessionV2 = FindAnyCreateSession();
        Assert.IsNotNull(sessionV2);

        var res0 = sessionV2.SendCommand(new FirmwareRequests.SetModeRequest(FirmwareRequests.Mode.Wifi), TimeSpan.FromSeconds(5));
        Assert.IsTrue(res0.IsSuccess);
        var resRestart = sessionV2.SendCommand(new FirmwareRequests.RestartDeviceRequest(), TimeSpan.FromSeconds(5));
        Assert.IsTrue(resRestart.IsSuccess);
        sessionV2.Dispose();
        Thread.Sleep(5000); // give it some reload time

        sessionV2 = FindAnyCreateSession();
        Assert.IsNotNull(sessionV2);


        var res1 = sessionV2.SendCommand(new FirmwareRequests.GetDeviceModeRequestV2(), TimeSpan.FromSeconds(5));
        Assert.IsTrue(res1.IsSuccess);
        AssertAreEqualIgnoreCase(FirmwareRequests.Mode.Wifi.Value, res1.Value!.mode);

        var res2 = sessionV2.SendCommand(new FirmwareRequests.GetWifiStatusRequest(), TimeSpan.FromSeconds(5));
        Assert.IsTrue(res2.IsSuccess);

        var res3 = sessionV2.SendCommand(new FirmwareRequests.GetWhoAmIRequest(), TimeSpan.FromSeconds(5));
        Assert.IsTrue(res3.IsSuccess);

        var res4 = sessionV2.SendCommand(new FirmwareRequests.GetSerialRequest(), TimeSpan.FromSeconds(5));
        Assert.IsTrue(res4.IsSuccess);

        var res5 = sessionV2.SendCommand(new FirmwareRequests.ScanWifiRequest(), TimeSpan.FromSeconds(40));
        Assert.IsTrue(res5.IsSuccess);

        var res6 = sessionV2.SendCommand(new FirmwareRequests.SetWifiRequest("ballz", "balls"), TimeSpan.FromSeconds(5));
        Assert.IsTrue(res6.IsSuccess);


        var res7 = sessionV2.SendCommand(new FirmwareRequests.SetModeRequest(FirmwareRequests.Mode.UVC),
            TimeSpan.FromSeconds(5));
        Assert.IsTrue(res7.IsSuccess);
        resRestart = sessionV2.SendCommand(new FirmwareRequests.RestartDeviceRequest(), TimeSpan.FromSeconds(5));
        Assert.IsTrue(resRestart.IsSuccess);

        sessionV2.Dispose();
        Thread.Sleep(5000); // give it some reload time

        sessionV2 = FindAnyCreateSession();
        Assert.IsNotNull(sessionV2);

        var res8 = sessionV2.SendCommand(new FirmwareRequests.GetDeviceModeRequestV2(), TimeSpan.FromSeconds(5));
        Assert.IsTrue(res8.IsSuccess);
        AssertAreEqualIgnoreCase(FirmwareRequests.Mode.UVC.Value, res8.Value!.mode);

        sessionV2.Dispose();
    }
}
