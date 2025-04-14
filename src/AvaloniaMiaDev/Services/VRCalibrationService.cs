// VRCalibrationService.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AvaloniaMiaDev.Contracts;
using AvaloniaMiaDev.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace AvaloniaMiaDev.Services
{
    public class VrCalibrationService : IVRService
    {
        private static readonly string CalibratorPath = Path.Combine(AppContext.BaseDirectory, "Calibration", "gaze_overlay.exe");

        private ILogger<VrCalibrationService> _logger;
        private readonly LocalSettingsService _localSettingsService;
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private Dictionary<string, Action<string>> _responseHandlers;
        private Process _vrProcess;

        // Event to notify subscribers about process output
        public event EventHandler<ProcessOutputEventArgs> ProcessOutputReceived;

        public VrCalibrationService(ILogger<VrCalibrationService> logger)
        {
            _logger = logger;
            _baseUrl = "http://localhost:23951";
            _httpClient = new HttpClient();

            _responseHandlers = new Dictionary<string, Action<string>>()
            {
                { "Error: (.*)", output => _logger.LogError(Regex.Match(output, "Error: (.*)").Groups[1].Value) },
            };
        }

        public async Task<VRCalibrationStatus> GetStatusAsync()
        {
            await StartVRProcess();
            var response = await _httpClient.GetStringAsync($"{_baseUrl}/settings");
            return JsonConvert.DeserializeObject<VRCalibrationStatus>(response)!;
        }

        public async Task Start(VRCalibration calibration)
        {
            var uri = new Uri($"{_baseUrl}/start_cameras?left={calibration.LeftEyeMjpegSource}?right={calibration.RightEyeMjpegSource}");
            _logger.LogInformation($"Starting VR Calibration for {uri}");
            await StartVRProcess();
        }

        public async Task<bool> StartCamerasAsync(VRCalibration calibration)
        {
            await StartVRProcess();

            await Task.Delay(2000);

            // Update the URLs to use the new streaming protocol
            var leftStreamUrl = $"udp://localhost:{calibration.LeftEyeMjpegSource.Split(':').Last()}";
            var rightStreamUrl = $"udp://localhost:{calibration.RightEyeMjpegSource.Split(':').Last()}";

            var response = await _httpClient.GetStringAsync(new Uri($"{_baseUrl}/start_cameras?left={leftStreamUrl}&right={rightStreamUrl}"));
            var result = JsonConvert.DeserializeObject<ApiResponse>(response);
            return result!.Result == "ok";
        }

        public async Task<bool> StartCalibrationAsync(VRCalibration calibration)
        {
            var url = $"{_baseUrl}/start_calibration?onnx_filename={Uri.EscapeDataString(calibration.ModelSavePath + VRCalibration.ModelName)}&routine_id={calibration.CalibrationInstructions}";
            var response = await _httpClient.GetStringAsync(url);
            var result = JsonConvert.DeserializeObject<ApiResponse>(response);
            return result!.Result == "ok";
        }

        public async Task<bool> StartPreviewAsync(VRCalibration calibration)
        {
            var modelPath = Uri.EscapeDataString(calibration.ModelSavePath + VRCalibration.ModelName);
            var url = $"{_baseUrl}/start_preview?model_path={Uri.EscapeDataString(modelPath)}";
            var response = await _httpClient.GetStringAsync(url);
            var result = JsonConvert.DeserializeObject<ApiResponse>(response);
            return result!.Result == "ok";
        }

        public async Task<bool> StopPreviewAsync()
        {
            var response = await _httpClient.GetStringAsync($"{_baseUrl}/stop_preview");
            var result = JsonConvert.DeserializeObject<ApiResponse>(response);
            return result!.Result == "ok";
        }

        private async Task StartVRProcess()
        {
            if (!File.Exists(CalibratorPath))
            {
                throw new FileNotFoundException("VR calibration executable not found", CalibratorPath);
            }

            if (Process.GetProcesses().Any(p => p.ProcessName == Path.GetFileNameWithoutExtension(CalibratorPath)))
            {
                return;
            }

            _vrProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = CalibratorPath,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    //RedirectStandardOutput = true,
                    //RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            _vrProcess.OutputDataReceived += ProcessOutputHandler;
            _vrProcess.ErrorDataReceived += ProcessErrorHandler;
            _vrProcess.Exited += (sender, args) =>
            {
                _vrProcess = null;
            };

            _vrProcess.Start();
            // _vrProcess.BeginOutputReadLine();
            // _vrProcess.BeginErrorReadLine();
        }

        private void ProcessOutputHandler(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data))
                return;

            // Notify subscribers
            ProcessOutputReceived?.Invoke(this, new ProcessOutputEventArgs(e.Data, false));

            // Process against known patterns
            _logger.LogInformation(e.Data);
            ProcessOutput(e.Data);
        }

        private void ProcessErrorHandler(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data))
                return;

            // Notify subscribers
            ProcessOutputReceived?.Invoke(this, new ProcessOutputEventArgs(e.Data, true));

            // Process against known patterns (errors might contain important information too)
            _logger.LogError(e.Data);
            ProcessOutput(e.Data);
        }

        private void ProcessOutput(string output)
        {
            foreach (var handler in _responseHandlers)
            {
                if (Regex.IsMatch(output, handler.Key, RegexOptions.IgnoreCase))
                {
                    handler.Value.Invoke(output);
                    break; // Stop after first match, or remove this line to allow multiple matches
                }
            }
        }

        // Add a method to dispose of resources properly
        public void Dispose()
        {
            if (_vrProcess != null && !_vrProcess.HasExited)
            {
                try
                {
                    _vrProcess.Kill();
                    _vrProcess.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error disposing VR process: {ex.Message}");
                }
                _vrProcess = null;
            }
        }
    }

    public class ProcessOutputEventArgs : EventArgs
    {
        public string Output { get; }
        public bool IsError { get; }
        public DateTime Timestamp { get; }

        public ProcessOutputEventArgs(string output, bool isError)
        {
            Output = output;
            IsError = isError;
            Timestamp = DateTime.Now;
        }
    }

    public class ApiResponse
    {
        [JsonProperty("result")]
        public string Result { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }

    public class VRCalibrationStatus
    {
        [JsonProperty("running")]
        public string Running { get; set; }

        [JsonProperty("recording")]
        public string Recording { get; set; }

        [JsonProperty("calibrationComplete")]
        public string CalibrationComplete { get; set; }

        [JsonProperty("currentIndex")]
        public int CurrentIndex { get; set; }

        [JsonProperty("maxIndex")]
        public int MaxIndex { get; set; }

        public bool IsRunning => Running == "1";
        public bool IsRecording => Recording == "1";
        public bool IsCalibrationComplete => CalibrationComplete == "1";
        public double Progress => MaxIndex > 0 ? (double)CurrentIndex / MaxIndex : 0;
    }
}
