using System;
using System.Threading.Tasks;
using Avalonia;
using Serilog;
using Velopack;

namespace Kiriha;

sealed partial class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Kiriha.Infrastructure.Platform.PathHelper.EnsureDirectoriesExist();

        bool isPlayer = Array.Exists(args, arg => arg.Equals("--player", StringComparison.OrdinalIgnoreCase));

        System.Threading.Mutex? mutex = null;
        System.Threading.Mutex? playerMutex = null;

        try
        {
            if (!TryEnsureSingleInstance(isPlayer, args, out mutex, out playerMutex))
            {
                return;
            }

            InitializeLogging(args);

            try
            {
                // Velopack startup logic
                VelopackApp.Build().Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Velopack startup error!");
            }

            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
            {
                var ex = eventArgs.ExceptionObject as Exception;
                Log.Fatal(ex, "Critical Error (UnhandledException)! Terminating={Terminating}", eventArgs.IsTerminating);

                if (eventArgs.IsTerminating)
                    Kiriha.Infrastructure.CrashReporter.WriteCrash(ex, "AppDomain.UnhandledException");
            };

            TaskScheduler.UnobservedTaskException += (sender, eventArgs) =>
            {
                Log.Warning(eventArgs.Exception, "Unobserved task exception (non-fatal, swallowed)");
                eventArgs.SetObserved();
            };

            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Critical Error during application startup or execution!");
                Kiriha.Infrastructure.CrashReporter.WriteCrash(ex, "Program.Main");
                throw;
            }
        }
        finally
        {
            Log.CloseAndFlush();
            playerMutex?.Dispose();
            mutex?.Dispose();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new Win32PlatformOptions
            {
                CompositionMode = new[] { Win32CompositionMode.WinUIComposition }
            })
            .LogToTrace();
}
