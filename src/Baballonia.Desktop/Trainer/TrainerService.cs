using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Microsoft.Extensions.Logging;
using OverlaySDK.Packets;

namespace Baballonia.Desktop.Trainer;

public class TrainerService : ITrainerService
{
    private readonly object _lock = new();

    private readonly ILogger<TrainerService> _logger;
    private Process? trainerProcess;
    public event Action<TrainerProgressReportPacket>? OnProgress;

    public TrainerService(ILogger<TrainerService> logger)
    {
        _logger = logger;
    }

    TrainerProgressReportPacket? ParseBatch(string line)
    {
        var pattern = @"Batch\s+(\d+)/(\d+),\s+Loss:\s+([0-9.]+)";
        var match = Regex.Match(line, pattern);
        if (!match.Success)
            return null;
        _logger.LogDebug(line);

        int currentBatch = int.Parse(match.Groups[1].Value);
        int totalBatches = int.Parse(match.Groups[2].Value);
        double loss = double.Parse(match.Groups[3].Value);

        return new TrainerProgressReportPacket("Batch", currentBatch, totalBatches, loss);
    }

    TrainerProgressReportPacket? ParseEpoch(string line)
    {
        var pattern = @"Epoch\s+(\d+)/(\d+)\s+completed\s+in\s+([\d.]+)s\.\s+Average\s+loss:\s+([\d.eE+-]+)";
        var match = Regex.Match(line, pattern);
        if (!match.Success)
            return null;

        _logger.LogDebug(line);

        int currentEpoch = int.Parse(match.Groups[1].Value);
        int totalEpochs = int.Parse(match.Groups[2].Value);
        double loss = double.Parse(match.Groups[3].Value);

        return new TrainerProgressReportPacket("Epoch", currentEpoch, totalEpochs, loss);
    }

    bool ParseTrainingComplete(string line)
    {
        var pattern = @"\s*Training\s+completed\s+successfully!\s*";
        var match = Regex.Match(line, pattern);

        _logger.LogDebug(line);

        return match.Success;
    }

    void NewLineEventHandler(object sender, DataReceivedEventArgs dataReceivedEventArgs)
    {
        _logger.LogDebug(dataReceivedEventArgs.Data);
        if (dataReceivedEventArgs.Data == null)
            return;

        var progress = ParseBatch(dataReceivedEventArgs.Data);
        if (progress == null)
            progress = ParseEpoch(dataReceivedEventArgs.Data);

        if (progress != null)
        {
            OnProgress?.Invoke(progress);
        }

        var isCompleted = ParseTrainingComplete(dataReceivedEventArgs.Data);
        if (isCompleted)
            return;
    }

    public void RunTraining(string usercalbinPath, string outputfilePath)
    {
        if (!File.Exists(usercalbinPath))
            throw new FileNotFoundException(usercalbinPath + " not found");

        string trainerPath = "BabbleTrainer.exe";
        if (!File.Exists(trainerPath))
            throw new FileNotFoundException(trainerPath + " not found");


        lock (_lock)
        {
            if (trainerProcess != null && trainerProcess.HasExited)
                trainerProcess = null;

            if (trainerProcess != null)
                throw new Exception("Training process already running");

            string launchArgs = usercalbinPath + " " + outputfilePath;
            var startInfo = new ProcessStartInfo
            {
                FileName = trainerPath,
                Arguments = launchArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };

            trainerProcess = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };
            trainerProcess.OutputDataReceived += NewLineEventHandler;
            trainerProcess.Exited += (sender, args) => { Interlocked.Exchange(ref trainerProcess, null); };

            trainerProcess.Start();
            trainerProcess.BeginOutputReadLine();
        }
    }

    public Task WaitAsync()
    {
        Process? process;
        lock (_lock)
        {
            process = trainerProcess;
        }

        return process != null
            ? process.WaitForExitAsync()
            : Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            trainerProcess?.Kill();
        }
    }
}
