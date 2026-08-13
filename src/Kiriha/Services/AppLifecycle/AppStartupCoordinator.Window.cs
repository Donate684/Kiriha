using Kiriha.ViewModels.Main;
using Kiriha.ViewModels.NowPlaying;
using Kiriha.ViewModels.Dialogs;
using Kiriha.ViewModels.Startup;
using Kiriha.ViewModels.Settings;
using Kiriha.Services.Data.Settings;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Kiriha.Services.Data;
using Kiriha.ViewModels;
using Kiriha.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Kiriha.Services.AppLifecycle;

public sealed partial class AppStartupCoordinator
{
    private void InitializeMainWindow(
        IClassicDesktopStyleApplicationLifetime desktop,
        Kiriha.Core.Services.ISettingsService settings,
        string[] args)
    {
        if (settings.NeedsFirstStartup())
        {
            var setupVm = _serviceProvider.GetRequiredService<FirstStartupViewModel>();
            setupVm.SetupCompleted += OnSetupCompleted;

            desktop.MainWindow = new FirstStartupWindow { DataContext = setupVm };
            return;
        }

        var mainWindowVm = _serviceProvider.GetRequiredService<MainWindowViewModel>();
        desktop.MainWindow = new MainWindow(settings) { DataContext = mainWindowVm };

        var startMinimized = args.Any(arg => arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        var hideToTrayOnStart = startMinimized && settings.Current.System.MinimizeToTray;

        if (startMinimized)
        {
            desktop.MainWindow.ShowInTaskbar = !hideToTrayOnStart;
            desktop.MainWindow.WindowState = WindowState.Minimized;
        }

        desktop.MainWindow.Loaded += (_, _) =>
        {
            Log.Information("StartupTiming: main window loaded elapsedMs={ElapsedMs}", _startupStopwatch.ElapsedMilliseconds);
            Dispatcher.UIThread.Post(
                () => Log.Information("StartupTiming: first render-priority callback elapsedMs={ElapsedMs}", _startupStopwatch.ElapsedMilliseconds),
                DispatcherPriority.Render);

            Dispatcher.UIThread.Post(async () =>
            {
                await InitializeAppServicesAsync();
                if (hideToTrayOnStart)
                    desktop.MainWindow!.Hide();
            }, DispatcherPriority.Background);
        };
    }

    private async void OnSetupCompleted()
    {
        try
        {
            if (_app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is FirstStartupWindow setupWindow)
            {
                if (setupWindow.DataContext is FirstStartupViewModel setupVm)
                    setupVm.SetupCompleted -= OnSetupCompleted;

                var mainWindowVm = _serviceProvider.GetRequiredService<MainWindowViewModel>();
                var main = new MainWindow(_serviceProvider.GetRequiredService<Kiriha.Core.Services.ISettingsService>()) { DataContext = mainWindowVm };
                main.Show();
                desktop.MainWindow = main;
                setupWindow.Close();

                await InitializeAppServicesAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "App: OnSetupCompleted failed; app will continue but services may be in an inconsistent state.");
        }
    }

    private async Task InitializeAppServicesAsync()
    {
        await _serviceProvider.GetRequiredService<AppReadinessService>().StartAsync();
        ShowPendingCrashReport();
    }
}
