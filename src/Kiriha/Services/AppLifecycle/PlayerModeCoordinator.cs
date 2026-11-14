using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Kiriha.Infrastructure.Player;
using Kiriha.Services.Data.Metadata;
using Kiriha.Services.Data.Settings;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Kiriha.Services.AppLifecycle;

public sealed partial class PlayerModeCoordinator : IDisposable
{
    private readonly Application _app;
    private readonly IServiceProvider _serviceProvider;
    private readonly TrayService _trayService;
    private readonly ShutdownCoordinator? _shutdownCoordinator;
    private PlayerCommandServer? _playerCommandServer;

    public PlayerModeCoordinator(Application app, IServiceProvider serviceProvider, TrayService trayService, ShutdownCoordinator? shutdownCoordinator = null)
    {
        _app = app;
        _serviceProvider = serviceProvider;
        _trayService = trayService;
        _shutdownCoordinator = shutdownCoordinator;
    }

    public void Dispose()
    {
        _playerCommandServer?.Dispose();
        _playerCommandServer = null;
    }

    public void Initialize(string[] args)
    {
        if (_app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        _trayService.DisableTrayIcons();
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (_shutdownCoordinator != null)
        {
            desktop.ShutdownRequested += _shutdownCoordinator.OnShutdownRequested;
        }
        else
        {
            desktop.ShutdownRequested += OnShutdownRequested;
        }
        StartPlayerCommandServer();

        var settingsService = _serviceProvider.GetRequiredService<SettingsService>();
        var localization = _serviceProvider.GetRequiredService<LocalizationService>();
        Kiriha.Core.Domain.Models.Entities.AnimeEntityPresentation.DefaultLocalizer = localization;
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
