using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Baballonia.Attributes;

// ReSharper disable InconsistentNaming

namespace Baballonia.Models
{
    public class FirmwareResponse<T>
    {
        public bool IsSuccess { get; }
        public T? Value { get; }
        public string? Error { get; }

        private FirmwareResponse(T value)
        {
            IsSuccess = true;
            Value = value;
        }

        private FirmwareResponse(string error)
        {
            IsSuccess = false;
            Error = error;
        }

        public static FirmwareResponse<T> Success(T value) => new(value);

        public static FirmwareResponse<T> Failure(string error) => new(error);

        public T GetValueOrThrow()
        {
            if (!IsSuccess)
                throw new InvalidOperationException($"Cannot access value. Error: {Error}");
            return Value!;
        }
    }
    public interface IFirmwareRequest
    {
        string command { get; }
        object? data { get; }
    }


    public interface IFirmwareRequest<TResponse>
    {
        string command { get; }
        object? data { get; }
    }

    public class FirmwareResponses
    {

        public record Error(string error);
        public record Heartbeat(string heartbeat, string serial);
        public class WifiNetwork
        {
            [JsonPropertyName("ssid")] public string Ssid { get; set; }

            [JsonPropertyName("channel")] public int Channel { get; set; }

            [JsonPropertyName("rssi")] public int Rssi { get; set; }

            [JsonPropertyName("mac_address")] public string MacAddress { get; set; }

            [JsonPropertyName("auth_mode")] public int AuthMode { get; set; }
        }

        public class WifiNetworkResponse
        {
            [JsonPropertyName("networks")] public required List<WifiNetwork> Networks { get; set; }
        }

        public class WifiStatusResponse
        {
            [JsonPropertyName("status")] public string Status { get; set; }

            [JsonPropertyName("networks_configured")]
            public int NetworksConfigured { get; set; }

            [JsonPropertyName("ip_address")] public string? IpAddress { get; set; }
        }
        public record WhoAmIResponse(string who_am_i, string version);
        public record GetSerialResponse(string mac, string serial);

        public record GetDeviceModeResponse(string mode, int value);

        public record GenericResponse(List<string> results);

        public record GenericResult(string result);

        public record GenericDataResult(string status, JsonDocument? data);
        public record GenericCommandResultV2(string command, GenericDataResult result);
        public record GenericResponseV2(List<GenericCommandResultV2> results);

    }

    public class FirmwareRequests
    {
        [ApiVersionRange("0.0.1")]
        public record RestartDeviceRequest() : IFirmwareRequest
        {
            public string command => "restart_device";
            public object? data => null;
        }
        [ApiVersionRange("0.0.1")]
        public record GetSerialRequest() : IFirmwareRequest<FirmwareResponses.GetSerialResponse>
        {
            public string command => "get_serial";
            public object? data => null;
        }

        [ApiVersionRange("0.0.1")]
        public record GetWhoAmIRequest() : IFirmwareRequest<FirmwareResponses.WhoAmIResponse>
        {
            public string command => "get_who_am_i";
            public object? data => null;
        }
        [ApiVersionRange("0.0.0")]
        public record ScanWifiRequest() : IFirmwareRequest<FirmwareResponses.WifiNetworkResponse>
        {
            public string command => "scan_networks";
            public object? data => null;
        }


        [ApiVersionRange("0.0.0")]
        public record SetWifiRequest(string ssid, string password) : IFirmwareRequest
        {
            public string command => "set_wifi";
            public object? data => new { name = "main", ssid = ssid, password = password, channel = 0, power = 0 };
        }

        [ApiVersionRange("0.0.0")]
        public record SetMdns(string mdns) : IFirmwareRequest
        {
            public string command => "set_mdns";
            public object? data => new { hostname = mdns };
        }

        [ApiVersionRange("0.0.1")]
        public record GetDeviceModeRequestV2 : IFirmwareRequest<FirmwareResponses.GetDeviceModeResponse>
        {
            public string command => "get_device_mode";
            public object? data => null;
        }

        [ApiVersionRange("0.0.0", "0.0.0")]
        public record SetPausedRequest(bool state) : IFirmwareRequest
        {
            public string command => "pause";
            public object? data => new { pause = state };
        }

        [ApiVersionRange("0.0.0")]
        public record GetWifiStatusRequest : IFirmwareRequest<FirmwareResponses.WifiStatusResponse>
        {
            public string command => "get_wifi_status";
            public object? data => null;
        }

        [ApiVersionRange("0.0.0")]
        public record ConnectWifiRequest : IFirmwareRequest
        {
            public string command => "connect_wifi";
            public object? data => null;
        }

        /**
         * quoting a txt I got
         * "NOTE: DO NOT USE START STREAMING COMMAND IT IS DEPRECATED"
         * for new firmware only ofc
         */
        [ApiVersionRange("0.0.0", "0.0.0")]
        public record StartStreamingRequest : IFirmwareRequest
        {
            public string command => "start_streaming";
            public object? data => null;
        }

        [ApiVersionRange("0.0.0")]
        public record GetDeviceModeRequest : IFirmwareRequest
        {
            public string command => "get_device_mode";
            public object? data => null;
        }

        [ApiVersionRange("0.0.0")]
        public record SetModeRequest(Mode Mode) : IFirmwareRequest
        {
            public string command => "switch_mode";
            public object? data => new { mode = Mode.Value };
        }


        // No string enums, cope :(
        public class Mode
        {
            private Mode(string value)
            {
                Value = value;
            }

            public string Value { get; private set; }

            public static Mode Wifi
            {
                get { return new Mode("wifi"); }
            }

            public static Mode UVC
            {
                get { return new Mode("uvc"); }
            }

            public static Mode Auto
            {
                get { return new Mode("auto"); }
            }
        }
    }
}
