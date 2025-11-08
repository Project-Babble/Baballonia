using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Helpers;
using Microsoft.Extensions.Logging;

namespace Baballonia.Models;

/// <summary>
/// Thread safe, async supported Session object for sending and receiving commands in json format
/// </summary>
public class FirmwareSessionV1 : IVersionedFirmwareSession, IDisposable
{
    private ICommandSender _commandSender;
    private ILogger _logger;
    // this is legacy by default so it will always stay as 0.0.0
    public Version Version { get; set; } = new Version(0, 0, 0);

    JsonExtractor jsonExtractor = new JsonExtractor();

    private SemaphoreSlim _lock = new(1, 1);

    JsonSerializerOptions _options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public FirmwareSessionV1(ICommandSender commandSender, ILogger logger)
    {
        _commandSender = commandSender;
        _logger = logger;
    }

    private bool JsonHasPrefix(JsonDocument json, string key)
    {
        if (json.RootElement.ValueKind != JsonValueKind.Object) return false;

        foreach (var prop in json.RootElement.EnumerateObject())
        {
            if (prop.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void SendCommand(string command)
    {
        var payload = command;
        _logger.LogDebug("Sending payload: {}", payload);
        _commandSender.WriteLine(payload);
    }

    private JsonDocument? ReadResponse(string responseJsonRootKey, TimeSpan timeout)
    {
        while (true)
        {
            Thread.Sleep(10); // give it some breathing time

            JsonDocument json = jsonExtractor.ReadUntilValidJson(() => _commandSender.ReadLine(timeout), timeout);
            _logger.LogDebug("Received json: {}", json.RootElement.GetRawText());
            if (JsonHasPrefix(json, responseJsonRootKey))
                return json;
            if (JsonHasPrefix(json, "error"))
            {
                var err = JsonSerializer.Deserialize<FirmwareResponses.Error>(json);
                _logger.LogError(err.error);
                return null;
            }
        }
    }

    public FirmwareResponses.Heartbeat? WaitForHeartbeat()
    {
        return WaitForHeartbeat(new TimeSpan(5000));
    }

    public FirmwareResponses.Heartbeat? WaitForHeartbeat(TimeSpan timeout)
    {
        _lock.Wait();
        try
        {
            var startTime = DateTime.Now;
            while (true)
            {
                if (DateTime.Now - startTime > timeout)
                    throw new TimeoutException("Timeout reached");

                var res = ReadResponse("heartbeat", timeout);
                return res?.Deserialize<FirmwareResponses.Heartbeat>();
            }
        }
        catch (TimeoutException ex)
        {
            _logger.LogError("Timeout reached");
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public FirmwareResponse<JsonDocument> SendCommand(IFirmwareRequest request, TimeSpan timeout)
    {
        RequestVersionGuard.ValidateRequestForVersion(request, Version);

        _lock.Wait();
        try
        {
            var genericReqList = new { commands = new[] { request } };
            var serialized = JsonSerializer.Serialize(genericReqList, _options);
            SendCommand(serialized);
            var jsonDoc = ReadResponse("results", timeout);
            var response = jsonDoc?.Deserialize<FirmwareResponses.GenericResponse>();
            if (response == null)
                return FirmwareResponse<JsonDocument>.Failure("Wtf?");

            var result =  JsonSerializer.Deserialize<FirmwareResponses.GenericResult>(response.results.First());

            return FirmwareResponse<JsonDocument>.Success(JsonSerializer.Deserialize<JsonDocument>(result!.result)!);
        }
        catch (TimeoutException ex)
        {
            return FirmwareResponse<JsonDocument>.Failure("Timeout reached");
        }
        finally
        {
            _lock.Release();
        }
    }

    public FirmwareResponse<T> SendCommand<T>(IFirmwareRequest<T> request, TimeSpan timeout)
    {
        RequestVersionGuard.ValidateRequestForVersion(request, Version);

        _lock.Wait();
        try
        {
            var genericReqList = new { commands = new[] { request } };

            var serialized = JsonSerializer.Serialize(genericReqList, _options);
            SendCommand(serialized);

            // special case because a list of networks comes in a separate json
            if (typeof(T) == typeof(FirmwareResponses.WifiNetworkResponse))
            {
                var networks = ReadResponse("networks", timeout);
                if (networks == null)
                    return FirmwareResponse<T>.Failure("No networks found");

                ReadResponse("results", timeout); // to discard the actual response
                return FirmwareResponse<T>.Success(networks.Deserialize<T>()!);
            }

            var jsonDoc = ReadResponse("results", timeout);
            var response = jsonDoc?.Deserialize<FirmwareResponses.GenericResponse>();
            if (response == null)
                return default;

            var result =  JsonSerializer.Deserialize<FirmwareResponses.GenericResult>(response.results.First());
            if (result == null)
                return FirmwareResponse<T>.Failure(response.ToString());

            return FirmwareResponse<T>.Success(JsonSerializer.Deserialize<T>(result.result)!);
        }
        catch (TimeoutException ex)
        {
            return FirmwareResponse<T>.Failure("Timeout reached");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<FirmwareResponse<T>> SendCommandAsync<T>(IFirmwareRequest<T> request, TimeSpan timeSpan)
    {
        RequestVersionGuard.ValidateRequestForVersion(request, Version);

        return await Task.Run(() =>
            SendCommand(request, timeSpan)
        );
    }

    public async Task<FirmwareResponse<JsonDocument>> SendCommandAsync(IFirmwareRequest request, TimeSpan timeSpan)
    {
        RequestVersionGuard.ValidateRequestForVersion(request, Version);

        return await Task.Run(() =>
            SendCommand(request, timeSpan)
        );
    }

    public async Task<FirmwareResponses.Heartbeat?> WaitForHeartbeatAsync()
    {
        return await Task.Run(() => WaitForHeartbeat());
    }

    public async Task<FirmwareResponses.Heartbeat?> WaitForHeartbeatAsync(TimeSpan timeout)
    {
        return await Task.Run(() => WaitForHeartbeat(timeout));
    }

    public void Dispose()
    {
        if (_commandSender != null)
            _commandSender.Dispose();
    }
}
