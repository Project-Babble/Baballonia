using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Baballonia.Contracts;
using Baballonia.ViewModels.SplitViewPane;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baballonia.Desktop.Calibration.Aero;

public partial class AeroOverlayTrainerCombo : IVROverlay, IVRCalibrator
{
    private ILogger Logger;

    private readonly Dictionary<string, Process> _activeProcesses = new();
    private readonly HttpClient _httpClient = new();
    private readonly string _baseUrl = "http://localhost:23951";

    private static void DeleteCaptureFiles(string directoryPath)
    {
        // Validate directory exists
        if (!Directory.Exists(directoryPath))
            return;

        // Get all files matching the capture pattern
        string[] filesToDelete = Directory.GetFiles(directoryPath, "capture.bin");

        // Delete each file
        foreach (string file in filesToDelete)
        {
            File.Delete(file);
        }
    }

    public void Dispose()
    {
        StopAllProcesses();
        _httpClient?.Dispose();
    }
}
