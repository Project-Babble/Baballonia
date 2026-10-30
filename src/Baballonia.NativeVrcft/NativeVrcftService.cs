using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using Baballonia.Assets;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VRCFaceTracking;
using VRCFaceTracking.Core;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.mDNS;
using VRCFaceTracking.Core.Models;
using VRCFaceTracking.Core.Models.ParameterDefinition;
using VRCFaceTracking.Core.OSC.Query.mDNS;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Services;
using IBabbleDispatcherService = Baballonia.Contracts.IDispatcherService;
using IBabbleSettingsService = Baballonia.Contracts.ILocalSettingsService;
using INativeVrcftService = Baballonia.Contracts.INativeVrcftService;

namespace Baballonia.NativeVrcft;

/// <summary>
/// Hosts VRCFaceTracking.Core's parameter pipeline (avatar parsing, OSCQuery, parameter sending) in-process.
/// The Core services live in their own container, built on start and disposed on stop, so they don't
/// clash with Baballonia's own OSC services of the same names.
/// </summary>
public sealed partial class NativeVrcftService(
    ILogger<NativeVrcftService> logger,
    ILoggerFactory loggerFactory,
    IBabbleSettingsService settingsService,
    IBabbleDispatcherService dispatcherService)
    : ObservableObject, INativeVrcftService
{
    // VRCFT keeps its mDNS sockets in static fields, so a second instance would throw. Share one across restarts.
    private static MulticastDnsService? _multicastDnsService;

    // Baballonia expression name -> Unified Expressions shapes. Mirrors the VRCFaceTracking.Baballonia module.
    private static readonly Dictionary<string, UnifiedExpressions[]> FaceShapes = new()
    {
        { "CheekPuffLeft", [UnifiedExpressions.CheekPuffLeft] },
        { "CheekPuffRight", [UnifiedExpressions.CheekPuffRight] },
        { "CheekSuckLeft", [UnifiedExpressions.CheekSuckLeft] },
        { "CheekSuckRight", [UnifiedExpressions.CheekSuckRight] },
        { "JawOpen", [UnifiedExpressions.JawOpen] },
        { "JawForward", [UnifiedExpressions.JawForward] },
        { "JawLeft", [UnifiedExpressions.JawLeft] },
        { "JawRight", [UnifiedExpressions.JawRight] },
        { "NoseSneerLeft", [UnifiedExpressions.NoseSneerLeft] },
        { "NoseSneerRight", [UnifiedExpressions.NoseSneerRight] },
        { "MouthFunnel", [UnifiedExpressions.LipFunnelLowerLeft, UnifiedExpressions.LipFunnelLowerRight, UnifiedExpressions.LipFunnelUpperLeft, UnifiedExpressions.LipFunnelUpperRight] },
        { "MouthPucker", [UnifiedExpressions.LipPuckerLowerLeft, UnifiedExpressions.LipPuckerLowerRight, UnifiedExpressions.LipPuckerUpperLeft, UnifiedExpressions.LipPuckerUpperRight] },
        { "MouthLeft", [UnifiedExpressions.MouthUpperLeft, UnifiedExpressions.MouthLowerLeft] },
        { "MouthRight", [UnifiedExpressions.MouthUpperRight, UnifiedExpressions.MouthLowerRight] },
        { "MouthRollUpper", [UnifiedExpressions.LipSuckUpperLeft, UnifiedExpressions.LipSuckUpperRight] },
        { "MouthRollLower", [UnifiedExpressions.LipSuckLowerLeft, UnifiedExpressions.LipSuckLowerRight] },
        { "MouthShrugUpper", [UnifiedExpressions.MouthRaiserUpper] },
        { "MouthShrugLower", [UnifiedExpressions.MouthRaiserLower] },
        { "MouthClose", [UnifiedExpressions.MouthClosed] },
        { "MouthSmileLeft", [UnifiedExpressions.MouthCornerPullLeft] },
        { "MouthSmileRight", [UnifiedExpressions.MouthCornerPullRight] },
        { "MouthFrownLeft", [UnifiedExpressions.MouthFrownLeft] },
        { "MouthFrownRight", [UnifiedExpressions.MouthFrownRight] },
        { "MouthDimpleLeft", [UnifiedExpressions.MouthDimpleLeft] },
        { "MouthDimpleRight", [UnifiedExpressions.MouthDimpleRight] },
        { "MouthUpperUpLeft", [UnifiedExpressions.MouthUpperUpLeft] },
        { "MouthUpperUpRight", [UnifiedExpressions.MouthUpperUpRight] },
        { "MouthLowerDownLeft", [UnifiedExpressions.MouthLowerDownLeft] },
        { "MouthLowerDownRight", [UnifiedExpressions.MouthLowerDownRight] },
        { "MouthPressLeft", [UnifiedExpressions.MouthPressLeft] },
        { "MouthPressRight", [UnifiedExpressions.MouthPressRight] },
        { "MouthStretchLeft", [UnifiedExpressions.MouthStretchLeft] },
        { "MouthStretchRight", [UnifiedExpressions.MouthStretchRight] },
        { "TongueOut", [UnifiedExpressions.TongueOut] },
        { "TongueUp", [UnifiedExpressions.TongueUp] },
        { "TongueDown", [UnifiedExpressions.TongueDown] },
        { "TongueLeft", [UnifiedExpressions.TongueLeft] },
        { "TongueRight", [UnifiedExpressions.TongueRight] },
        { "TongueRoll", [UnifiedExpressions.TongueRoll] },
        { "TongueBendDown", [UnifiedExpressions.TongueBendDown] },
        { "TongueCurlUp", [UnifiedExpressions.TongueCurlUp] },
        { "TongueSquish", [UnifiedExpressions.TongueSquish] },
        { "TongueFlat", [UnifiedExpressions.TongueFlat] },
        { "TongueTwistLeft", [UnifiedExpressions.TongueTwistLeft] },
        { "TongueTwistRight", [UnifiedExpressions.TongueTwistRight] },
    };

    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private ServiceProvider? _provider;
    private OscRecvService? _recvService;
    private BinaryFaceDataSender? _binaryFaceDataSender;
    private OscQueryService? _oscQueryService;
    private ParameterSenderService? _parameterSender;
    private Timer? _externalVrcftTimer;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private bool _externalVrcftDetected;

    public bool IsSupported => true;

    public async Task StartAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            if (_provider != null)
            {
                return;
            }

            logger.LogInformation("Starting native VRCFaceTracking");
            VRChat.EnsureVRCOSCDirectory();

            _multicastDnsService ??= new MulticastDnsService(loggerFactory.CreateLogger<MulticastDnsService>());
            _provider = BuildServices(_multicastDnsService);

            // Constructing the mutator registers it as UnifiedTracking.Mutator. We never Load() it, so it stays
            // disabled and passes data through untouched; calibration is done by Baballonia before it gets here.
            _provider.GetRequiredService<UnifiedTrackingMutator>();

            // The sender must exist before the OSC target is loaded, as it binds its socket on the target's change event
            _parameterSender = _provider.GetRequiredService<ParameterSenderService>();
            ParameterSenderService.Clear();

            _recvService = _provider.GetRequiredService<OscRecvService>();
            await _recvService.StartAsync(CancellationToken.None);

            _binaryFaceDataSender = _provider.GetRequiredService<BinaryFaceDataSender>();
            await _binaryFaceDataSender.StartAsync(CancellationToken.None);

            _oscQueryService = _provider.GetRequiredService<OscQueryService>();
            _oscQueryService.PropertyChanged += OnOscQueryPropertyChanged;
            await _oscQueryService.InitializeAsync();

            _externalVrcftTimer = new Timer(_ => CheckForExternalVrcft(), null, TimeSpan.Zero, TimeSpan.FromSeconds(5));

            OnUiThread(() =>
            {
                IsRunning = true;
                Status = Resources.Settings_OSC_NativeVRCFT_Status_Waiting;
            });
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to start native VRCFaceTracking");
            await TeardownAsync();
            throw;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            if (_provider == null)
            {
                return;
            }

            logger.LogInformation("Stopping native VRCFaceTracking");
            await TeardownAsync();
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public void SetExpression(string name, float value)
    {
        var data = UnifiedTracking.Data;
        switch (name)
        {
            case "LeftEyeX": data.Eye.Left.Gaze.x = value; return;
            case "LeftEyeY": data.Eye.Left.Gaze.y = value; return;
            case "LeftEyeLid": data.Eye.Left.Openness = value; return;
            case "RightEyeX": data.Eye.Right.Gaze.x = value; return;
            case "RightEyeY": data.Eye.Right.Gaze.y = value; return;
            case "RightEyeLid": data.Eye.Right.Openness = value; return;
        }

        if (!FaceShapes.TryGetValue(name, out var shapes))
        {
            return;
        }

        foreach (var shape in shapes)
        {
            data.Shapes[(int)shape].Weight = value;
        }
    }

    public async Task FlushAsync(CancellationToken ct)
    {
        var sender = _parameterSender;
        if (sender == null)
        {
            return;
        }

        // Runs every relevant avatar parameter against the latest data, which enqueues the changed ones
        UnifiedTracking.UpdateDataSync();
        await sender.FlushAsync(ct);
    }

    private ServiceProvider BuildServices(MulticastDnsService multicastDnsService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton<ILocalSettingsService>(new VrcftSettingsAdapter(settingsService));
        services.AddSingleton<IDispatcherService>(new VrcftDispatcherAdapter(dispatcherService));
        services.AddSingleton(multicastDnsService);

        services.AddSingleton<IOscTarget, OscTarget>();
        services.AddSingleton<AvatarConfigParser>();
        services.AddSingleton<OscQueryConfigParser>();
        services.AddSingleton<HttpHandler>();
        services.AddSingleton<OscSendService>();
        services.AddSingleton<OscRecvService>();
        services.AddSingleton<UnifiedTrackingMutator>();
        services.AddSingleton<ParameterSenderService>();
        services.AddSingleton<OscQueryService>();
        services.AddSingleton<BinaryFaceDataSender>();
        return services.BuildServiceProvider();
    }

    private async Task TeardownAsync()
    {
        _externalVrcftTimer?.Dispose();
        _externalVrcftTimer = null;
        _parameterSender = null;

        if (_oscQueryService != null)
        {
            _oscQueryService.PropertyChanged -= OnOscQueryPropertyChanged;
            _oscQueryService = null;
        }

        // The shared mDNS service would otherwise call back into the disposed OscQueryService
        if (_multicastDnsService != null)
        {
            _multicastDnsService.OnVrcClientDiscovered = () => { };
        }

        if (_binaryFaceDataSender != null)
        {
            await _binaryFaceDataSender.StopAsync(CancellationToken.None);
            _binaryFaceDataSender = null;
        }

        if (_recvService != null)
        {
            await _recvService.StopAsync(CancellationToken.None);
            // OscRecvService never closes its socket. Rebinding to an ephemeral port releases the
            // OSC receive port (9001) so a standalone VRCFaceTracking can take it over.
            _recvService.UpdateTarget(new IPEndPoint(IPAddress.Loopback, 0));
            _recvService = null;
        }

        // Parameters are static; make them all irrelevant so the current avatar isn't sent on the next start
        var noAvatar = new NullAvatarDef("None", "none");
        foreach (var parameter in UnifiedTracking.AllParameters)
        {
            parameter.ResetParam([], noAvatar);
        }

        ParameterSenderService.Clear();

        if (_provider != null)
        {
            await _provider.DisposeAsync();
            _provider = null;
        }

        OnUiThread(() =>
        {
            IsRunning = false;
            Status = null;
            ExternalVrcftDetected = false;
        });
    }

    private void OnOscQueryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not OscQueryService { AvatarParameters: not null } oscQuery)
        {
            return;
        }

        if (e.PropertyName is not (nameof(OscQueryService.AvatarInfo) or nameof(OscQueryService.AvatarParameters)))
        {
            return;
        }

        var status = string.Format(Resources.Settings_OSC_NativeVRCFT_Status,
            oscQuery.AvatarInfo.Name, oscQuery.AvatarParameters.Count);
        OnUiThread(() => Status = status);
    }

    private void CheckForExternalVrcft()
    {
        var processes = Process.GetProcessesByName("VRCFaceTracking");
        var detected = processes.Length > 0;
        foreach (var process in processes)
        {
            process.Dispose();
        }

        if (detected == ExternalVrcftDetected)
        {
            return;
        }

        if (detected)
        {
            logger.LogWarning("VRCFaceTracking is running while native VRCFaceTracking is enabled. VRChat will receive parameters twice.");
        }

        OnUiThread(() => ExternalVrcftDetected = detected);
    }

    // Bound to the settings page, so property changes must be raised on the UI thread
    private void OnUiThread(Action action) => dispatcherService.Run(action);
}
