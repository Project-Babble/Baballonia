using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using Baballonia.Contracts;
using Microsoft.Extensions.Logging;

namespace Baballonia.Models;

public class FirmwareSessionFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<FirmwareSessionFactory> _logger;
    private readonly ICommandSenderFactory _commandSenderFactory;

    public FirmwareSessionFactory(ILoggerFactory loggerFactory, ICommandSenderFactory commandSenderFactory)
    {
        _loggerFactory = loggerFactory;
        _commandSenderFactory = commandSenderFactory;
        _logger = loggerFactory.CreateLogger<FirmwareSessionFactory>();
    }

    private string[] FindAvailableSerialPorts()
    {
        // GetPortNames() may return single port multiple times
        // https://stackoverflow.com/questions/33401217/serialport-getportnames-returns-same-port-multiple-times
        return SerialPort.GetPortNames().Distinct().ToArray();
    }

    public IEnumerable<IFirmwareSession> TryOpenAllSessions()
    {
        List<IFirmwareSession> sessions = [];
        var avaliablePorts = FindAvailableSerialPorts();
        foreach (var port in avaliablePorts)
        {
            var s = TryOpenSession(port);
            if(s != null)
                sessions.Add(s);
        }

        return sessions;
    }

    public IFirmwareSession? TryOpenSession(string port)
    {
        var sessionv2 = TryOpenV2Session(port);
        if (sessionv2 != null)
            return sessionv2;

        var sessionV1 = TryOpenV1Session(port);
        if (sessionV1 != null)
            return sessionV1;

        _logger.LogInformation($"{port} most likely is not a babble board");

        return null;
    }

    private FirmwareSessionV2? TryOpenV2Session(string port)
    {
        _logger.LogInformation($"Attempting to open V2 session for {port}");
        var sender = _commandSenderFactory.Create(CommandSenderType.Serial, port);
        var sessionV2 = new FirmwareSessionV2(sender, _loggerFactory.CreateLogger<FirmwareSessionV2>());
        var response = sessionV2.SendCommand(new FirmwareRequests.GetWhoAmIRequest(), TimeSpan.FromSeconds(3));
        if (!response.IsSuccess)
        {
            _logger.LogInformation($"Can't open V2 session for {port}");
            sessionV2.Dispose();
            sender.Dispose();
            return null;
        }

        _logger.LogInformation($"Opened V2 session for {port}");
        return sessionV2;
    }

    private FirmwareSessionV1? TryOpenV1Session(string port)
    {
        _logger.LogInformation($"Attempting to open V1 session for {port}");
        var sender = _commandSenderFactory.Create(CommandSenderType.Serial, port);
        var sessionV1 = new FirmwareSessionV1(sender, _loggerFactory.CreateLogger<FirmwareSessionV1>());
        var heartbeat = sessionV1.WaitForHeartbeat(TimeSpan.FromSeconds(3));
        if (heartbeat == null)
        {
            _logger.LogInformation($"Can't open V1 session for {port}");
            sessionV1.Dispose();
            sender.Dispose();
            return null;
        }

        _logger.LogInformation($"Opened V1 session for {port}");
        return sessionV1;
    }
}
