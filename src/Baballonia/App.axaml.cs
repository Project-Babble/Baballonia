using System;
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

namespace Baballonia;

public class App : Application
{
    public static IVROverlay Overlay;
    public static IVRCalibrator Calibrator;
    public static IDeviceEnumerator DeviceEnumerator;
    public static Type PlatformConnectorType;

    private IHost? _host;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // https://github.com/benaclejames/VRCFaceTracking/blob/51405d57cbbd46c92ff176d5211d043ed875ad42/VRCFaceTracking/App.xaml.cs#L61C9-L71C10
        // Check for a "reset" file in the root of the app directory.
        // If one is found, wipe all files from inside it and delete the file.
        var resetFile = Path.Combine(Utils.PersistentDataDirectory, "reset");
        if (File.Exists(resetFile))
        {
            // Delete everything including files and folders in Utils.PersistentDataDirectory
            foreach (var file in Directory.EnumerateFiles(Utils.PersistentDataDirectory, "*", SearchOption.AllDirectories))
            {
                File.Delete(file);
            }
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var locator = new ViewLocator();
        DataTemplates.Add(locator);

        var hostBuilder = Host.CreateDefaultBuilder().
            ConfigureServices((_, services) =>
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
                    services.AddTransient<VrcViewModel>();
                    services.AddTransient<VrcView>();
                    services.AddTransient<FirmwareViewModel>();
                    services.AddTransient<FirmwareView>();
                    services.AddTransient<OnboardingViewModel>();
                    services.AddTransient<OnboardingView>();
                }

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
                var defaultSettings = Path.Combine(AppContext.BaseDirectory, LocalSettingsService.DefaultLocalSettingsFile);
                Directory.CreateDirectory(Path.GetDirectoryName(settingsLocation)!);
                File.Copy(defaultSettings, settingsLocation);
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
                    Assembly.
                        GetExecutingAssembly().
                        GetManifestResourceNames().
                        First(x => x.Contains(model)),
                    modelPath,
                    overwrite: false);
            }
        }

        _host = hostBuilder.Build();
        Ioc.Default.ConfigureServices(_host.Services);

        // Hacky
        if (Utils.IsSupportedDesktopOS)
        {
            if (DeviceEnumerator != null && DeviceEnumerator.GetType().Name == "DesktopDeviceEnumerator")
            {
                var appLogger = Ioc.Default.GetService<ILogger<App>>();

                try
                {
                    // Use reflection to create DesktopDeviceEnumerator with logger since we can't reference Baballonia.Desktop here
                    var deviceEnumeratorType = DeviceEnumerator.GetType();

                    if (deviceEnumeratorType == null)
                    {
                        appLogger?.LogWarning("DeviceEnumerator type is null, cannot reinitialize with logger");
                        return;
                    }

                    appLogger?.LogInformation("Attempting to (re)initialize DeviceEnumerator of type: {TypeName}", deviceEnumeratorType.FullName);

                    // Create generic logger type safely
                    Type loggerType;
                    try
                    {
                        loggerType = typeof(ILogger<>).MakeGenericType(deviceEnumeratorType);
                    }
                    catch (Exception ex)
                    {
                        appLogger?.LogError(ex, "Failed to create generic logger type for DeviceEnumerator: {TypeName}", deviceEnumeratorType.FullName);
                        return;
                    }

                    // Attempt to get the logger service
                    var deviceLogger = Ioc.Default.GetService(loggerType);
                    appLogger?.LogInformation("Logger service resolution result. Logger found: {LoggerFound}", deviceLogger != null);

                    if (deviceLogger != null)
                    {
                        // Attempt to create new instance with logger using reflection
                        try
                        {
                            var newDeviceEnumerator = Activator.CreateInstance(deviceEnumeratorType, deviceLogger);

                            if (newDeviceEnumerator is IDeviceEnumerator deviceEnum)
                            {
                                DeviceEnumerator = deviceEnum;
                                appLogger?.LogInformation("DeviceEnumerator successfully reinitialized with logger");
                            }
                            else
                            {
                                appLogger?.LogError("Created instance does not implement IDeviceEnumerator interface. Type: {TypeName}", newDeviceEnumerator?.GetType().FullName ?? "null");
                            }
                        }
                        catch (Exception ex)
                        {
                            appLogger?.LogError(ex, "Failed to create DeviceEnumerator instance via reflection. This could be due to missing constructor, constructor exceptions, or security restrictions. Type: {TypeName}", deviceEnumeratorType.FullName);
                        }
                    }
                    else
                    {
                        appLogger?.LogWarning("Could not resolve logger service for DeviceEnumerator type: {TypeName}. DeviceEnumerator will continue without enhanced logging.", deviceEnumeratorType.FullName);
                    }
                }
                catch (Exception ex)
                {
                    appLogger?.LogError(ex, "Unexpected error during DeviceEnumerator logger initialization. DeviceEnumerator will continue with existing instance.");
                }
            }
        }


        Assembly assembly = Assembly.GetExecutingAssembly();
        Version version = assembly.GetName().Version!;
        var logger = Ioc.Default.GetService<ILogger<MainWindow>>();
        logger?.LogInformation($"Baballonia version {version} starting...");

        Task.Run(async () => await _host.StartAsync());

        var activation = Ioc.Default.GetRequiredService<IActivationService>();
        Task.Run(async () => await activation.ActivateAsync(null!));

        var vm = Ioc.Default.GetRequiredService<MainViewModel>();
        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow(vm);
                desktop.MainWindow.Loaded += (_, _) =>
                {
                    desktop.MainWindow.ShowOnboardingIfNeeded();
                };
                desktop.ShutdownRequested += OnShutdown;
                break;
            case ISingleViewApplicationLifetime singleViewPlatform:
                singleViewPlatform.MainView = new MainView { DataContext = vm };
                break;
        }

        _host.Services.GetRequiredService<IThemeSelectorService>().SetRequestedThemeAsync();

        base.OnFrameworkInitializationCompleted();
    }

    private void OnShutdown(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime) return;

        var vrcft = Ioc.Default.GetRequiredService<IMainService>();
        Task.Run(vrcft.Teardown);

        var loop = Ioc.Default.GetRequiredService<ProcessingLoopService>();
        loop.FaceProcessingPipeline.VideoSource?.Dispose();
        loop.EyesProcessingPipeline.VideoSource?.Dispose();

        var settings = Ioc.Default.GetRequiredService<ILocalSettingsService>();
        settings.ForceSave();

        Overlay.Dispose();
    }

    private void OnTrayShutdownClicked(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}

