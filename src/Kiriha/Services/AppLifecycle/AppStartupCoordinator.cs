using Kiriha.Services.Data.Metadata;
using Kiriha.Services.Data.Image;
using Kiriha.Services.Data.Settings;
using System;
using Kiriha.Core.Domain.Models;
using System.Diagnostics;
using AsyncImageLoader;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Kiriha.Core;
using Kiriha.Services.Data;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Kiriha.Services.AppLifecycle;

public sealed partial class AppStartupCoordinator
{
    private readonly Application _app;
    private readonly IServiceProvider _serviceProvider;
    private readonly TrayService _trayService;
    private readonly ShutdownCoordinator _shutdownCoordinator;
    private readonly Stopwatch _startupStopwatch = Stopwatch.StartNew();

    public AppStartupCoordinator(
        Application app,
        IServiceProvider serviceProvider,
        TrayService trayService,
        ShutdownCoordinator shutdownCoordinator)
    {
        _app = app;
        _serviceProvider = serviceProvider;
        _trayService = trayService;
        _shutdownCoordinator = shutdownCoordinator;
    }

    public void Initialize(string[] args)
    {
        var sw = Stopwatch.StartNew();
        var settings = _serviceProvider.GetRequiredService<SettingsService>();
        Log.Information("StartupTiming: settings resolved elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);

        sw.Restart();
        _app.RequestedThemeVariant = settings.Current.UI.Theme switch
        {
            ThemeType.Light => ThemeVariant.Light,
            ThemeType.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        Log.Information("StartupTiming: theme applied elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);

        sw.Restart();
        var loc = _serviceProvider.GetRequiredService<LocalizationService>();
        loc.LoadLanguage(settings.Current.UI.LanguageCode);
        Log.Information("StartupTiming: localization loaded elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);

        sw.Restart();
        var imageCache = _serviceProvider.GetRequiredService<ImageCacheService>();
        ImageLoader.AsyncImageLoader = new KirihaImageLoader(imageCache);
        CachedImage.Initialize(imageCache);
        Log.Information("StartupTiming: image services initialized elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);

        if (_app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += _shutdownCoordinator.OnShutdownRequested;
            InitializeMainWindow(desktop, settings, args);
        }

        _trayService.UpdateTrayMenu();
        Log.Information("StartupTiming: app coordinator initialized elapsedMs={ElapsedMs}", _startupStopwatch.ElapsedMilliseconds);
    }
}
