using Kiriha.Services.Data.Settings;
using Kiriha.Services.Data.Metadata;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Kiriha.Infrastructure.Player;
using Kiriha.Services.Data;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Kiriha.Services.AppLifecycle;

public sealed partial class PlayerModeCoordinator
{
    private readonly Application _app;
    private readonly IServiceProvider _serviceProvider;
    private readonly TrayService _trayService;
    private PlayerCommandServer? _playerCommandServer;

    public PlayerModeCoordinator(Application app, IServiceProvider serviceProvider, TrayService trayService)
    {
        _app = app;
        _serviceProvider = serviceProvider;
        _trayService = trayService;
    }

    public void Initialize(string[] args)
    {
        if (_app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        _trayService.DisableTrayIcons();
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        desktop.ShutdownRequested += OnShutdownRequested;
        StartPlayerCommandServer();

        var settingsService = _serviceProvider.GetRequiredService<SettingsService>();
        var localization = _serviceProvider.GetRequiredService<LocalizationService>();
        localization.LoadLanguage(settingsService.Current.UI.LanguageCode);

        if (!PlayerProcessBridge.IsResident(args))
            desktop.MainWindow = CreatePlayerWindow(args);
    }

    private void StartPlayerCommandServer()
    {
        _playerCommandServer ??= new PlayerCommandServer(args =>
        {
            Dispatcher.UIThread.Post(() => HandlePlayerCommand(args));
        });
        _playerCommandServer.Start();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        FlushSettings();
    }

    private void FlushSettings()
    {
        try
        {
            _serviceProvider.GetRequiredService<SettingsService>().SaveImmediate();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Player shutdown: failed to save settings");
        }
    }
}
