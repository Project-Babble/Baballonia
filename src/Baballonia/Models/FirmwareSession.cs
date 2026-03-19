using Baballonia.Contracts;
using Baballonia.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Baballonia.Models;

/// <summary>
/// Thread safe, async supported Session object for sending and receiving commands in json format
/// </summary>
public class FirmwareSessionV1(ICommandSender commandSender, ILogger logger) : IVersionedFirmwareSession, IDisposable
{
    // this is legacy by default so it will always stay as 0.0.0
    public Version Version { get; set; } = new(0, 0, 0);

    private readonly JsonExtractor _jsonExtractor = new();

    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static bool JsonHasPrefix(JsonDocument json, string key)
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
        logger.LogDebug("Sending payload: {}", payload);
        commandSender.WriteLine(payload);
    }

    private JsonDocument? ReadResponse(string responseJsonRootKey, TimeSpan timeout)
    {
        while (true)
        {
            Thread.Sleep(10); // give it some breathing time

            var json = _jsonExtractor.ReadUntilValidJson(() => commandSender.ReadLine(timeout), timeout);
            logger.LogDebug("Received json: {}", json.RootElement.GetRawText());
            if (JsonHasPrefix(json, responseJsonRootKey))
                return json;
            if (!JsonHasPrefix(json, "error")) continue;

            var err = json.Deserialize<FirmwareResponses.Error>();
            logger.LogError(err.error);
            return null;
        }
    }

    private FirmwareResponses.Heartbeat? WaitForHeartbeat()
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
        catch (TimeoutException)
        {
            logger.LogError("Timeout reached");
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
            var serialized = JsonSerializer.Serialize(genericReqList, Options);
            SendCommand(serialized);
            var jsonDoc = ReadResponse("results", timeout);
            var response = jsonDoc?.Deserialize<FirmwareResponses.GenericResponse>();
            if (response == null)
                return FirmwareResponse<JsonDocument>.Failure("Wtf?");

            try
            {
                // Attempt to extract inner content
                var result = JsonSerializer.Deserialize<FirmwareResponses.GenericResult>(response.results.First());
                return FirmwareResponse<JsonDocument>.Success(
                    JsonSerializer.Deserialize<JsonDocument>(result!.result)!);
            }
            catch (JsonException)
            {
                // Attempt to extract outer content
                return FirmwareResponse<JsonDocument>.Success(
                    JsonSerializer.Deserialize<JsonDocument>(response.results.First())!);
            }
        }
        catch (TimeoutException)
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

            var serialized = JsonSerializer.Serialize(genericReqList, Options);
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
                return FirmwareResponse<T>.Failure("Invalid response from tracker");

            var result =  JsonSerializer.Deserialize<FirmwareResponses.GenericResult>(response.results.First());
            return result != null
                ? FirmwareResponse<T>.Success(JsonSerializer.Deserialize<T>(result.result)!)
                : FirmwareResponse<T>.Failure(response.ToString());
        }
        catch (TimeoutException)
        {
            return FirmwareResponse<T>.Failure("Timeout reached");
        }
        catch (Exception any)
        {
            return FirmwareResponse<T>.Failure(any.Message);
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
        if (commandSender != null)
            commandSender.Dispose();
    }
}
