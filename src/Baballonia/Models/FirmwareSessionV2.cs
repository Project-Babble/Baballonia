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

public class FirmwareSessionV2(ICommandSender commandSender, ILogger logger) : IVersionedFirmwareSession, IDisposable
{
    // default to minimal required version for which this Session is expected to work
    // will be overridden by factory if needed
    public Version Version { get; set; } = new(0, 0, 1);

    private readonly JsonExtractor _jsonExtractor = new();

    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private FirmwareResponses.GenericResponseV2? ReadResponse(TimeSpan timeout)
    {
        var json = _jsonExtractor.ReadUntilValidJson(() => commandSender.ReadLine(timeout), timeout);
        logger.LogDebug("Received json: {}", json.RootElement.GetRawText());
        var response = json.Deserialize<FirmwareResponses.GenericResponseV2>();
        return response ?? null;
    }

    private void SendCommand(string command)
    {
        var payload = command + "\n";
        logger.LogDebug("Sending payload: {}", payload);
        commandSender.WriteLine(payload);
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

            var response = ReadResponse(timeout);
            if (response == null)
                return FirmwareResponse<JsonDocument>.Failure("Wtf? how did this happen?");
            var result = response.results.First().result;
            return result.status == "success"
                ? FirmwareResponse<JsonDocument>.Success(result.data)
                : FirmwareResponse<JsonDocument>.Failure("Something went wrong in the board");
        }
        catch (TimeoutException)
        {
            return FirmwareResponse<JsonDocument>.Failure("Timeout reached");
        }
        catch (Exception any)
        {
            return FirmwareResponse<JsonDocument>.Failure(any.Message);
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

            var response = ReadResponse(timeout);
            if (response == null)
                return FirmwareResponse<T>.Failure("Wtf? how did this happen?");
            var result = response.results.First().result;
            if (result.status == "success")
            {
                var deserialized = result.data!.Deserialize<T>()!;
                return FirmwareResponse<T>.Success(deserialized);
            }

            if (result is { status: "error", data.RootElement.ValueKind: JsonValueKind.String })
                return FirmwareResponse<T>.Failure(result.data.RootElement.GetString()!);

            return FirmwareResponse<T>.Failure($"Something went wrong: {result}");
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

    public async Task<FirmwareResponse<JsonDocument>> SendCommandAsync(IFirmwareRequest request,
        TimeSpan timeSpan)
    {
        RequestVersionGuard.ValidateRequestForVersion(request, Version);

        return await Task.Run(() =>
            SendCommand(request, timeSpan)
        );
    }


    public void Dispose()
    {
        if (commandSender != null)
            commandSender.Dispose();
    }
}
