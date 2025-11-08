using System;
using System.Text.Json;
using System.Threading.Tasks;
using Baballonia.Models;

namespace Baballonia.Contracts;

public interface IFirmwareSession
{
    FirmwareResponse<JsonDocument> SendCommand(IFirmwareRequest request, TimeSpan timeout);
    FirmwareResponse<T> SendCommand<T>(IFirmwareRequest<T> request, TimeSpan timeout);
    Task<FirmwareResponse<T>> SendCommandAsync<T>(IFirmwareRequest<T> request, TimeSpan timeSpan);
    Task<FirmwareResponse<JsonDocument>> SendCommandAsync(IFirmwareRequest request, TimeSpan timeSpan);
}
