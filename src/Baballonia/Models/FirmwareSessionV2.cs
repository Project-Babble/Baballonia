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

public class FirmwareSessionV2 : IVersionedFirmwareSession, IDisposable
{
    private ICommandSender _commandSender;
    private ILogger _logger;

    // default to minimal required version for which this Session is expected to work
    // will be overridden by factory if needed
    public Version Version { get; set; } = new Version(0, 0, 1);

    JsonExtractor jsonExtractor = new JsonExtractor();

    private SemaphoreSlim _lock = new(1, 1);

    JsonSerializerOptions _options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public FirmwareSessionV2(ICommandSender commandSender, ILogger logger)
    {
        _commandSender = commandSender;
        _logger = logger;
    }

    private FirmwareResponses.GenericResponseV2? ReadResponse(TimeSpan timeout)
    {
        JsonDocument json = jsonExtractor.ReadUntilValidJson(() => _commandSender.ReadLine(timeout), timeout);
        _logger.LogDebug("Received json: {}", json.RootElement.GetRawText());
        var response = json.Deserialize<FirmwareResponses.GenericResponseV2>();
        if (response == null)
            return null;

        return response;
    }

    private void SendCommand(string command)
    {
        var payload = command + "\n";
        _logger.LogDebug("Sending payload: {}", payload);
        _commandSender.WriteLine(payload);
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

            var response = ReadResponse(timeout);
            if (response == null)
                return FirmwareResponse<JsonDocument>.Failure("Wtf? how did this happen?");
            var result = response.results.First().result;
            if (result.status != "success")
            {
                return FirmwareResponse<JsonDocument>.Failure("Something went wrong in the board");
            }

            return FirmwareResponse<JsonDocument>.Success(result.data);
        }
        catch (TimeoutException ex)
        {
            return FirmwareResponse<JsonDocument>.Failure($"Timeout reached");
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

            var serialized = JsonSerializer.Serialize(genericReqList, _options);
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
        catch (TimeoutException ex)
        {
            return FirmwareResponse<T>.Failure($"Timeout reached");
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
        if (_commandSender != null)
            _commandSender.Dispose();
    }
}
