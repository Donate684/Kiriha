using System.Diagnostics;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Infrastructure.Player;
using Kiriha.Infrastructure.Tracking.Integration;
using Kiriha.Data.Core;
using Kiriha.Infrastructure.Settings;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Kiriha.Services.AppLifecycle;

public sealed class AppReadinessService
{
    private readonly IDatabaseInitializer _databaseInitializer;
    private readonly IAnimeRepository _animeRepo;
    private readonly IAnimeRefreshService _refreshService;
    private readonly IDiscordService _discordService;
    private readonly MaintenanceService _maintenanceService;
    private readonly ISettingsService _settingsService;
    private readonly ISeasonalHiddenRepository _seasonalHiddenRepo;
    private readonly ITorrentFilterRepository _torrentFilterRepo;
    private readonly IEnumerable<IHostedService> _hostedServices;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;

    private readonly TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();
    private Task? _startupTask;
    private AppReadinessState _state = AppReadinessState.NotStarted;

    public AppReadinessService(
        IDatabaseInitializer databaseInitializer,
        IAnimeRepository animeRepo,
        IAnimeRefreshService refreshService,
        IDiscordService discordService,
        MaintenanceService maintenanceService,
        ISettingsService settingsService,
        ISeasonalHiddenRepository seasonalHiddenRepo,
        ITorrentFilterRepository torrentFilterRepo,
        IEnumerable<IHostedService> hostedServices,
        IBackgroundTaskSupervisor backgroundTasks)
    {
        _databaseInitializer = databaseInitializer;
        _animeRepo = animeRepo;
        _refreshService = refreshService;
        _discordService = discordService;
        _maintenanceService = maintenanceService;
        _settingsService = settingsService;
        _seasonalHiddenRepo = seasonalHiddenRepo;
        _torrentFilterRepo = torrentFilterRepo;
        _hostedServices = hostedServices;
        _backgroundTasks = backgroundTasks;
    }

    public event EventHandler<AppReadinessState>? StateChanged;

    public AppReadinessState State
    {
        get
        {
            lock (_gate) return _state;
        }
    }

    public Task ReadyTask => _readyTcs.Task;

    public Task StartAsync()
    {
        lock (_gate)
        {
            _startupTask ??= StartCoreAsync();
            return _startupTask;
        }
    }

    private async Task StartCoreAsync()
    {
        SetState(AppReadinessState.Starting);
        var total = Stopwatch.StartNew();

        try
        {
            var stage = Stopwatch.StartNew();
            await _databaseInitializer.InitializeAsync();
            await _seasonalHiddenRepo.InitializeAsync();
            await _torrentFilterRepo.InitializeAsync();
            Log.Information("StartupTiming: readiness database stage elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

            stage.Restart();
            await _animeRepo.InitializeAsync();

            bool needsInitialRefresh = _animeRepo.Collection.Count == 0;
            Log.Information("StartupTiming: readiness anime stage elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

            stage.Restart();
            _discordService.Initialize();
            _maintenanceService.Start();
            Log.Information("StartupTiming: readiness foreground services stage elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

            stage.Restart();
            foreach (var hosted in _hostedServices)
            {
                try
                {
                    await hosted.StartAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to start hosted service {Type}", hosted.GetType().Name);
                }
            }
            Log.Information("StartupTiming: readiness hosted services stage elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

            stage.Restart();
            if (_settingsService.Current.System.KeepPlayerProcessAlive)
                PlayerProcessBridge.StartResident();
            Log.Information("StartupTiming: readiness resident player stage elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

            if (needsInitialRefresh)
            {
                _ = _backgroundTasks.Run("AppReadiness.InitialLibraryRefresh",
                    ct => _refreshService.RefreshAnimeListAsync(ct: ct));
            }

            SetState(AppReadinessState.Ready);
            _readyTcs.TrySetResult();
            Log.Information("StartupTiming: readiness complete elapsedMs={ElapsedMs}", total.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "App readiness failed during startup");
            SetState(AppReadinessState.Failed);
            _readyTcs.TrySetException(ex);
        }
    }

    private void SetState(AppReadinessState state)
    {
        lock (_gate)
        {
            if (_state == state) return;
            _state = state;
        }

        StateChanged?.Invoke(this, state);
    }
}
