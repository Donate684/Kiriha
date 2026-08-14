using Kiriha.Infrastructure.Tracking.Integration;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Core.Tracking.Core;
using Kiriha.Services.Data.Core;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Services.Data.Repository;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Services.Data.Settings;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Infrastructure.Player;
using Kiriha.Services.Data;
using Kiriha.Core.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Collections.Generic;

namespace Kiriha.Services.AppLifecycle;

public sealed class AppReadinessService
{
    private readonly DatabaseInitializer _databaseInitializer;
    private readonly AnimeRepository _animeRepo;
    private readonly AnimeSyncOrchestrator _orchestrator;
    private readonly NotificationService _notificationService;
    private readonly DiscordService _discordService;
    private readonly SmtcService _smtcService;
    private readonly MaintenanceService _maintenanceService;
    private readonly SettingsService _settingsService;
    private readonly IEnumerable<IHostedService> _hostedServices;

    private readonly TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private Task? _startupTask;
    private AppReadinessState _state = AppReadinessState.NotStarted;

    public AppReadinessService(
        DatabaseInitializer databaseInitializer,
        AnimeRepository animeRepo,
        AnimeSyncOrchestrator orchestrator,
        NotificationService notificationService,
        DiscordService discordService,
        SmtcService smtcService,
        MaintenanceService maintenanceService,
        SettingsService settingsService,
        IEnumerable<IHostedService> hostedServices)
    {
        _databaseInitializer = databaseInitializer;
        _animeRepo = animeRepo;
        _orchestrator = orchestrator;
        _notificationService = notificationService;
        _discordService = discordService;
        _smtcService = smtcService;
        _maintenanceService = maintenanceService;
        _settingsService = settingsService;
        _hostedServices = hostedServices;
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
            await _databaseInitializer.InitializationTask;
            Log.Information("StartupTiming: readiness database stage elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

            stage.Restart();
            await _animeRepo.InitializeAsync();
            await _animeRepo.InitializationTask;

            if (_animeRepo.Collection.Count == 0)
            {
                await _orchestrator.SyncWithTrackersAsync();
            }
            Log.Information("StartupTiming: readiness anime stage elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

            stage.Restart();
            _discordService.Initialize();
            await _smtcService.StartAsync();
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
