using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Kiriha.Infrastructure.Player;
using Kiriha.Services.Metadata;
using Kiriha.Infrastructure.Settings;
using Kiriha.Services.AppLifecycle.Shutdown;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Kiriha.Services.AppLifecycle;

public sealed partial class PlayerModeCoordinator : IDisposable
{
    private static readonly TimeSpan InitialCommandTimeout = TimeSpan.FromSeconds(12);
    private readonly Application _app;
    private readonly IServiceProvider _serviceProvider;
    private readonly TrayService _trayService;
    private readonly ShutdownCoordinator _shutdownCoordinator;
    private readonly PlayerWindowShutdown _playerWindows;
    private PlayerCommandServer? _playerCommandServer;
    private IDisposable? _initialCommandDeadline;

    public PlayerModeCoordinator(Application app, IServiceProvider serviceProvider, TrayService trayService, ShutdownCoordinator? shutdownCoordinator = null)
    {
        _app = app;
        _serviceProvider = serviceProvider;
        _trayService = trayService;
        _shutdownCoordinator = shutdownCoordinator ?? new ShutdownCoordinator(serviceProvider, serviceProvider.GetServices<IShutdownHandler>());
        _playerWindows = serviceProvider.GetService<PlayerWindowShutdown>() ?? new PlayerWindowShutdown();
    }

    public void Dispose()
    {
        _initialCommandDeadline?.Dispose();
        _initialCommandDeadline = null;
        _playerCommandServer?.Dispose();
        _playerCommandServer = null;
    }

    public void Initialize(string[] args)
    {
        if (_app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        _trayService.DisableTrayIcons();
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _shutdown = new PlayerShutdownSequence(_playerWindows.CloseAll, _shutdownCoordinator.DrainAsync);
        desktop.ShutdownRequested += OnPlayerShutdownRequested;
        StartPlayerCommandServer();

        var settingsService = _serviceProvider.GetRequiredService<SettingsService>();
        var localization = _serviceProvider.GetRequiredService<LocalizationService>();
        Kiriha.Core.Domain.Models.Entities.AnimeEntityPresentation.DefaultLocalizer = localization;
        localization.LoadLanguage(settingsService.Current.UI.LanguageCode);

        if (args.Contains(PlayerPlaybackProtocol.BootstrapArg, StringComparer.OrdinalIgnoreCase))
        {
            _initialCommandDeadline = DispatcherTimer.RunOnce(
                () => _ = CompleteLocalShutdownAsync(), InitialCommandTimeout);
        }
        else if (!PlayerProcessBridge.IsResident(args))
            OpenInitialWindow(desktop, args);
    }

    private void StartPlayerCommandServer()
    {
        _playerCommandServer ??= new PlayerCommandServer(
            args => Dispatcher.UIThread.Post(() => HandlePlayerCommand(args)),
            PrepareRemoteShutdownAsync, () => Dispatcher.UIThread.Post(FinishPlayerShutdown), ReceivePlaybackAsync);
        _playerCommandServer.Start();
    }

}
