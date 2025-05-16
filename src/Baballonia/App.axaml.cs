using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Baballonia.Helpers;
using Baballonia.Activation;
using Baballonia.Contracts;
using Baballonia.Models;
using Baballonia.Services;
using Baballonia.ViewModels;
using Baballonia.ViewModels.SplitViewPane;
using Baballonia.Views;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Jeek.Avalonia.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Baballonia;

public partial class App : Application
{
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
        Localizer.SetLocalizer(new JsonLocalizer());

        var locator = new ViewLocator();
        DataTemplates.Add(locator);

        var hostBuilder = Host.CreateDefaultBuilder().
            ConfigureServices((context, services) =>
            {
                services.AddLogging(logging =>
                {
                    logging.ClearProviders();
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
                services.AddSingleton<IEyeInferenceService, EyeInferenceService>();
                services.AddSingleton<IFaceInferenceService, FaceInferenceService>();
                services.AddSingleton<IVrService, VrCalibrationService>();

                services.AddSingleton<IActivationService, ActivationService>();
                services.AddSingleton<IDispatcherService, DispatcherService>();

                // Core Services
                services.AddTransient<IIdentityService, IdentityService>();
                services.AddTransient<IFileService, FileService>();
                services.AddSingleton<IOscTarget, OscTarget>();
                services.AddSingleton<OscRecvService>();
                services.AddSingleton<OscSendService>();
                services.AddSingleton<ParameterSenderService>();
                services.AddTransient<GithubService>();
                services.AddTransient<FirmwareService>();
                services.AddSingleton<IMainService, MainStandalone>();

                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();

                services.AddTransient<OutputPageViewModel>();
                services.AddTransient<OutputPageView>();
                services.AddTransient<AppSettingsViewModel>();
                services.AddTransient<AppSettingsView>();
                services.AddTransient<HomePageViewModel>();
                services.AddTransient<HomePageView>();
                services.AddTransient<EyeCalibrationViewModel>();
                services.AddTransient<EyeCalibrationView>();
                services.AddTransient<FaceCalibrationViewModel>();
                services.AddTransient<FaceCalibrationView>();
                services.AddTransient<OnboardingViewModel>();
                services.AddTransient<OnboardingView>();

                services.AddHostedService(provider => provider.GetService<OscRecvService>()!);
                services.AddHostedService(provider => provider.GetService<ParameterSenderService>()!);

                // Configuration
                IConfiguration config = new ConfigurationBuilder()
                    .AddJsonFile(LocalSettingsService.DefaultLocalSettingsFile)
                    .Build();
                services.Configure<LocalSettingsOptions>(config);
            });

        if (!File.Exists(LocalSettingsService.DefaultLocalSettingsFile))
        {
            // Create the file if it doesn't exist and write empty JSON "{}"
            // TODO Write defaults
            var path = Path.Combine(AppContext.BaseDirectory, LocalSettingsService.DefaultLocalSettingsFile);
            File.WriteAllText(path, "{}");
        }

        if (OperatingSystem.IsAndroid())
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

        // var notif = new NotificationModel();
        // notif.Title = "Hello, World!";
        // notif.Body = "This is a test notification.";
        // SendNotification?.Invoke(notif);

        base.OnFrameworkInitializationCompleted();
    }

    private void OnShutdown(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        var vrcft = Ioc.Default.GetRequiredService<IMainService>();
        Task.Run(vrcft.Teardown);

        var eye = Ioc.Default.GetRequiredService<IEyeInferenceService>();
        eye.Shutdown();

        var face = Ioc.Default.GetRequiredService<IFaceInferenceService>();
        face.Shutdown();
    }

    private void OnTrayShutdownClicked(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
