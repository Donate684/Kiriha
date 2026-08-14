using Microsoft.Extensions.DependencyInjection;
using Kiriha.ViewModels.Main;
using Kiriha.ViewModels.NowPlaying;
using Kiriha.ViewModels.Dialogs;
using Kiriha.ViewModels.Startup;
using Kiriha.ViewModels.Settings;
using System;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Infrastructure;
using Kiriha.ViewModels;
using Kiriha.Views;
using Serilog;

namespace Kiriha.Services.AppLifecycle;

public sealed partial class AppStartupCoordinator
{
    public static void InstallUnhandledExceptionHandler()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            if (e.Exception is ObjectDisposedException ode
                && ode.ObjectName != null
                && ode.ObjectName.Contains("Ref<Avalonia.Platform.IBitmapImpl>"))
            {
                Log.Warning(ode, "Swallowed AsyncImageLoader disposed-bitmap race in layout pass");
                e.Handled = true;
                return;
            }

            Log.Fatal(e.Exception, "Unhandled UI thread exception");
            CrashReporter.WriteCrash(e.Exception, "Dispatcher.UIThread.UnhandledException");
        };
    }

    private void ShowPendingCrashReport()
    {
        try
        {
            var pending = CrashReportReader.GetPendingCrashFile();
            if (string.IsNullOrEmpty(pending))
                return;

            if (_app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var crashWindow = new CrashReportWindow
                    {
                        DataContext = new CrashReportViewModel(pending, _serviceProvider.GetRequiredService<Kiriha.Core.Abstractions.Services.ILocalizer>())
                    };

                    if (desktop.MainWindow != null && desktop.MainWindow.IsVisible)
                        crashWindow.Show(desktop.MainWindow);
                    else
                        crashWindow.Show();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "ShowPendingCrashReport: window show failed");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ShowPendingCrashReport failed");
        }
    }
}
