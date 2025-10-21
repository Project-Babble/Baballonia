using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Baballonia.Helpers;
using Baballonia.Activation;
using Baballonia.Contracts;
using Baballonia.Factories;
using Baballonia.Models;
using Baballonia.Services;
using Baballonia.Services.Inference;
using Baballonia.Services.Inference.Platforms;
using Baballonia.ViewModels;
using Baballonia.ViewModels.SplitViewPane;
using Baballonia.Views;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCvSharp.XPhoto;

namespace Baballonia;

public class App : Application
{
    private IHost? _host;
    private bool IsTeardDown = false;
    private static Action<IServiceCollection> ConfigurePlatformServices { get; set; }
    private static Action<IServiceCollection>? _platformSpecifficServices;

    public static void RegisterRequiredPlatformServices<TOverlay, TDeviceEnumerator, TPlatformConnector>()
        where TOverlay : class, IVROverlay
        where TDeviceEnumerator : class, IDeviceEnumerator
        where TPlatformConnector : class, IPlatformConnector
    {
        ConfigurePlatformServices = services =>
        {
            services.AddSingleton<IVROverlay, TOverlay>();
            services.AddSingleton<IDeviceEnumerator, TDeviceEnumerator>();
            services.AddSingleton<IPlatformConnector, TPlatformConnector>();
        };
    }

    public static void RegisterPlatformSpecifficServices(Action<IServiceCollection> configure)
    {
        _platformSpecifficServices = configure;
    }


    public override void Initialize()
    {
        if (ConfigurePlatformServices == null)
            throw new ApplicationException("No platform services were provided provided");

        AvaloniaXamlLoader.Load(this);

        // https://github.com/benaclejames/VRCFaceTracking/blob/51405d57cbbd46c92ff176d5211d043ed875ad42/VRCFaceTracking/App.xaml.cs#L61C9-L71C10
        // Check for a "reset" file in the root of the app directory.
        // If one is found, wipe all files from inside it and delete the file.
        var resetFile = Path.Combine(Utils.PersistentDataDirectory, "reset");
        if (File.Exists(resetFile))
        {
            // Delete everything including files and folders in Utils.PersistentDataDirectory
            foreach (var file in Directory.EnumerateFiles(Utils.PersistentDataDirectory, "*",
                         SearchOption.AllDirectories))
            {
                File.Delete(file);
            }
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var locator = new ViewLocator();
        DataTemplates.Add(locator);

        var hostBuilder = Host.CreateDefaultBuilder().ConfigureServices((_, services) =>
        {
            services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogFileLogger.GetMinimumLogLevel());
                logging.AddDebug();
                logging.AddConsole();
                logging.AddProvider(new OutputLogProvider(Dispatcher.UIThread));
                logging.AddProvider(new LogFileProvider());
            });

            // Default Activation Handler
            services.AddTransient<ActivationHandler, DefaultActivationHandler>();
            services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

            services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
            services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
            services.AddSingleton<ILanguageSelectorService, LanguageSelectorService>();

            services.AddSingleton<IActivationService, ActivationService>();
            services.AddSingleton<IDispatcherService, DispatcherService>();
            services.AddSingleton<ProcessingLoopService>();

            services.AddSingleton<InferenceFactory>();
            services.AddSingleton<FaceProcessingPipeline>();
            services.AddSingleton<FacePipelineManager>();
            services.AddSingleton<IFacePipelineEventBus, FacePipelineEventBus>();
            services.AddSingleton<EyeProcessingPipeline>();
            services.AddSingleton<EyePipelineManager>();
            services.AddSingleton<IEyePipelineEventBus, EyePipelineEventBus>();
            services.AddSingleton<SingleCameraSourceFactory>();

            // Core Services
            services.AddTransient<IIdentityService, IdentityService>();
            services.AddTransient<IFileService, FileService>();
            services.AddSingleton<IOscTarget, OscTarget>();
            services.AddSingleton<OscRecvService>();
            services.AddSingleton<OscSendService>();
            services.AddTransient<OscQueryServiceWrapper>();
            services.AddSingleton<ParameterSenderService>();
            services.AddTransient<GithubService>();
            services.AddTransient<ICommandSenderFactory, CommandSenderFactory>();
            services.AddTransient<FirmwareService>();
            services.AddSingleton<IMainService, MainStandalone>();
            services.AddSingleton<ICalibrationService, CalibrationService>();
            services.AddSingleton<DropOverlayService>();

            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();

            services.AddTransient<HomePageViewModel>();
            services.AddTransient<HomePageView>();
            services.AddTransient<CalibrationViewModel>();
            services.AddTransient<CalibrationView>();
            services.AddTransient<OutputPageViewModel>();
            services.AddTransient<OutputPageView>();
            services.AddTransient<AppSettingsViewModel>();
            services.AddTransient<AppSettingsView>();

            if (Utils.IsSupportedDesktopOS)
            {
                services.AddSingleton<ICommandSenderFactory, CommandSenderFactory>();
                services.AddSingleton<ICommandSender, SerialCommandSender>();
                services.AddSingleton<OpenVRService>();
                services.AddTransient<VrcViewModel>();
                services.AddTransient<VrcView>();
                services.AddTransient<FirmwareViewModel>();
                services.AddTransient<FirmwareView>();
                services.AddTransient<OnboardingViewModel>();
                services.AddTransient<OnboardingView>();
            }

            ConfigurePlatformServices.Invoke(services);
            _platformSpecifficServices?.Invoke(services);

            services.AddHostedService(provider => provider.GetService<OscRecvService>()!);
            services.AddHostedService(provider => provider.GetService<ParameterSenderService>()!);

            // Configuration
            IConfiguration config = new ConfigurationBuilder()
                .AddJsonFile(LocalSettingsService.DefaultLocalSettingsFile, optional: true)
                .Build();
            services.Configure<LocalSettingsOptions>(config);
        });

        if (Utils.IsSupportedDesktopOS)
        {
            var settingsLocation = Path.Combine(
                Utils.PersistentDataDirectory,
                LocalSettingsService.DefaultApplicationDataFolder,
                LocalSettingsService.DefaultLocalSettingsFile);
            if (!File.Exists(settingsLocation))
            {
                // Create the settings file if it doesn't exist and copy the default settings file
                var defaultSettings =
                    Path.Combine(AppContext.BaseDirectory, LocalSettingsService.DefaultLocalSettingsFile);
                Directory.CreateDirectory(Path.GetDirectoryName(settingsLocation)!);
                File.Copy(defaultSettings, settingsLocation);
                if (!OperatingSystem.IsWindows())
                {
                    // Make file read-write if not on Windows as the source file might be in read-only.
                    File.SetUnixFileMode(
                        settingsLocation,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | File.GetUnixFileMode(settingsLocation));
                }
            }
        }
        else // extract default models for mobile
        {
            string[] models = ["eyeModel.onnx", "faceModel.onnx"];
            foreach (var model in models)
            {
                string modelPath = Path.Combine(AppContext.BaseDirectory, model);
                Utils.ExtractEmbeddedResource(
                    Assembly.GetExecutingAssembly(),
                    Assembly.GetExecutingAssembly().GetManifestResourceNames().First(x => x.Contains(model)),
                    modelPath,
                    overwrite: false);
            }
        }

        _host = hostBuilder.Build();
        Ioc.Default.ConfigureServices(_host.Services);

        // Initialize settings
        var localSettings = Ioc.Default.GetService<ILocalSettingsService>();

        Assembly assembly = Assembly.GetExecutingAssembly();
        Version version = assembly.GetName().Version!;
        var logger = Ioc.Default.GetService<ILogger<MainWindow>>();
        logger?.LogInformation($"Baballonia version {version} starting...");

        Task.Run(async () => await _host.StartAsync());

        var activation = Ioc.Default.GetRequiredService<IActivationService>();
        Task.Run(() => activation.Activate(null!));

        var vm = Ioc.Default.GetRequiredService<MainViewModel>();
        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow(vm);
                desktop.MainWindow.Loaded += (_, _) => { desktop.MainWindow.ShowOnboardingIfNeeded(); };
                desktop.Exit += (s, e) =>
                {
                    OnShutdown(s, e);
                    _host.Dispose();
                };
                desktop.ShutdownRequested += OnShutdown;
                break;
            case ISingleViewApplicationLifetime singleViewPlatform:
                singleViewPlatform.MainView = new MainView { DataContext = vm };
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnShutdown(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime) return;
        if (IsTeardDown) return;

        var mainService = Ioc.Default.GetService<IMainService>();
        var settings = Ioc.Default.GetService<ILocalSettingsService>();
        settings?.ForceSave();

        mainService?.Teardown();
        IsTeardDown = true;
    }

    private void OnTrayShutdownClicked(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
