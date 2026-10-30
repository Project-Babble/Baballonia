using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace Baballonia.Contracts;

/// <summary>
/// Runs VRCFaceTracking's parameter pipeline inside Baballonia, converting our expressions into
/// avatar parameters and sending them to VRChat directly (no external VRCFaceTracking needed).
/// </summary>
public interface INativeVrcftService : INotifyPropertyChanged
{
    /// <summary>Whether this platform can run native VRCFaceTracking at all.</summary>
    bool IsSupported { get; }

    bool IsRunning { get; }

    /// <summary>Human-readable status (current avatar, parameter count), or null when not running.</summary>
    string? Status { get; }

    /// <summary>True when a standalone VRCFaceTracking process is running alongside us.</summary>
    bool ExternalVrcftDetected { get; }

    Task StartAsync();

    Task StopAsync();

    /// <summary>
    /// Sets a calibrated expression by its Baballonia name, as used by
    /// <see cref="Services.ParameterSenderService.EyeExpressionMap"/> and <see cref="Services.ParameterSenderService.FaceExpressionMap"/>.
    /// </summary>
    void SetExpression(string name, float value);

    /// <summary>Converts the latest expressions into avatar parameters and sends them.</summary>
    Task FlushAsync(CancellationToken ct);
}

/// <summary>Fallback for platforms without native VRCFaceTracking support.</summary>
public class UnsupportedNativeVrcftService : INativeVrcftService
{
    public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }

    public bool IsSupported => false;
    public bool IsRunning => false;
    public string? Status => null;
    public bool ExternalVrcftDetected => false;

    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public void SetExpression(string name, float value) { }
    public Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
}
