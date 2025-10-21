using Avalonia;
using Baballonia.Desktop.Calibration.Aero;
using Baballonia.Desktop.Captures;
using System;
using System.Threading;
using Baballonia.Contracts;
using Baballonia.Desktop.Calibration;
using Baballonia.Desktop.Trainer;
using Microsoft.Extensions.DependencyInjection;
using Velopack;

namespace Baballonia.Desktop;

sealed class Program
{
    /* Baballonia needs to be single-instanced, because:
     * a) There's no real reason to have several Baballonia instances running at a time.
     * b) Some users have opened/minimized multiple Baballonia instances on accident, breaking OSC. This is no good!
     * In the future, a file-lock mechanism might prove more robust (not to mention Linux support?),
     * but a Mutex should do the job until we have reason to roll one ourselves. Sources:
     * https://stackoverflow.com/questions/6486195/ensuring-only-one-application-instance
     * https://github.com/AvaloniaUI/Avalonia/discussions/17854#discussioncomment-11700510 */
    private static readonly Mutex Mutex = new(false, "baballonia-unique-id");

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // Give the mutex some time to think, check if another process with an identical mutex ID exists
        if (!Mutex.WaitOne(TimeSpan.FromSeconds(2), false))
        {
            return 75; // Exit code for BSD's EX_TEMPFAIL, invite the user to try again later
        }

        VelopackApp.Build().Run();

        App.RegisterRequiredPlatformServices<
            OverlayTrainerService,
            DesktopDeviceEnumerator,
            DesktopConnector
        >();

        App.RegisterPlatformSpecifficServices(collection =>
        {
            collection.AddSingleton<IOverlayProgram, OverlayProgram>();
            collection.AddSingleton<ITrainerService, TrainerService>();
            collection.AddSingleton<EyeCaptureStepFactory>();
            collection.AddSingleton<EyeCalibration>();
        });

        try
        {
            var builder = BuildAvaloniaApp();
            return builder.StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Mutex.ReleaseMutex();
            Mutex.Dispose();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
