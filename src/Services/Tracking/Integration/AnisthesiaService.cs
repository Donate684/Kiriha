using Kiriha.Services.Tracking.Integration;
using Kiriha.Services.Tracking.Feed;
using Kiriha.Services.Tracking.Core;
using Kiriha.Services.Data.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking.Anisthesia;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Kiriha.Services.Tracking.Integration;

public class AnisthesiaService : IHostedService, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly SmtcService _smtcService;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;
    private readonly DetectionManager _detectionManager;
    private readonly PauseDetector _pauseDetector = new();
    private readonly List<AnisthesiaPlayer> _availablePlayers;
    private IReadOnlySet<string> _runningPlayerNames = new HashSet<string>();
    private uint _lastTrackedPid;
    private readonly CancellationTokenSource _disposeCts = new();

    public event EventHandler<ParsedMedia>? MediaDetected;
    public event EventHandler? MediaCleared;
    public event EventHandler<IReadOnlySet<string>>? RunningPlayersChanged;

    public List<AnisthesiaPlayer> AvailablePlayers => _availablePlayers;
    public IReadOnlySet<string> RunningPlayerNames => _runningPlayerNames;

    public AnisthesiaService(
        SettingsService settingsService,
        SmtcService smtcService,
        IBackgroundTaskSupervisor backgroundTasks,
        IReadOnlyList<AnisthesiaPlayer> players)
    {
        _settingsService = settingsService;
        _smtcService = smtcService;
        _backgroundTasks = backgroundTasks;
        
        _availablePlayers = new List<AnisthesiaPlayer>(players);
        _detectionManager = new DetectionManager(_availablePlayers, _settingsService);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _backgroundTasks.Run("AnisthesiaService.PollingLoop", async ct =>
        {
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
                await PollingLoopAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation
            }
        });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!_disposeCts.IsCancellationRequested)
            {
                _disposeCts.Cancel();
            }
        }
        catch (ObjectDisposedException) { }

        return Task.CompletedTask;
    }

    private async Task PollingLoopAsync(CancellationToken ct)
    {
        Log.Information("Anisthesia Polling Service started.");

        await _smtcService.StartAsync();

        ParsedMedia? lastDetected = null;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Update running players set
                var running = _detectionManager.GetRunningPlayerNames();

                if (!running.SetEquals(_runningPlayerNames))
                {
                    _runningPlayerNames = running;
                    RunningPlayersChanged?.Invoke(this, _runningPlayerNames);
                }

                var detected = await _detectionManager.DetectAsync();

                if (detected != null)
                {
                    var timeline = _smtcService.GetTimeline(detected.ProcessName);
                    if (timeline.HasValue)
                    {
                        detected.Position = timeline.Value.Position;
                        detected.Duration = timeline.Value.Duration;
                    }

                    // Reconcile the strategy's optimistic IsPlaying=true against
                    // the audio session state. WindowTitleStrategy and HandleEnumerationStrategy
                    // can't tell pause from play on their own — the player process
                    // and the open file handle look identical in both cases. The
                    // audio session, however, goes Inactive within ~1 s of pause
                    // for every mainstream player, so we treat that as the
                    // authoritative signal (debounced inside PauseDetector to
                    // tolerate quiet scenes / silent intros).
                    if (detected.Pid != 0)
                    {
                        if (_lastTrackedPid != 0 && _lastTrackedPid != detected.Pid)
                        {
                            // The user switched to a different player instance —
                            // drop the previous tracker so we don't carry over
                            // its Inactive streak.
                            _pauseDetector.Forget(_lastTrackedPid);
                        }
                        _lastTrackedPid = detected.Pid;

                        var audioState = AudioSessionProbe.GetStateForPid(detected.Pid);
                        var contextLabel = string.IsNullOrEmpty(detected.Episode)
                            ? detected.AnimeTitle
                            : $"{detected.AnimeTitle} ep {detected.Episode}";
                        detected.IsPlaying = _pauseDetector.Update(detected.Pid, audioState, contextLabel);
                    }

                    // Update only if title or episode or playing state changed
                    if (lastDetected == null ||
                        lastDetected.AnimeTitle != detected.AnimeTitle ||
                        lastDetected.Episode != detected.Episode ||
                        lastDetected.IsPlaying != detected.IsPlaying)
                    {
                        lastDetected = detected;
                        MediaDetected?.Invoke(this, detected);
                    }
                }
                else if (lastDetected != null)
                {
                    if (_lastTrackedPid != 0)
                    {
                        _pauseDetector.Forget(_lastTrackedPid);
                        _lastTrackedPid = 0;
                    }
                    lastDetected = null;
                    MediaCleared?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Anisthesia polling error: {Msg}", ex.Message);
            }

            int delayMs = 5000;
            if (lastDetected != null)
                delayMs = 2000; // Actively tracking media, poll fast to catch pause/play
            else if (_runningPlayerNames.Count > 0)
                delayMs = 3000; // Player is open, waiting for media

            await Task.Delay(delayMs, ct);
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_disposeCts.IsCancellationRequested)
            {
                _disposeCts.Cancel();
            }
        }
        catch (ObjectDisposedException) { }

        _disposeCts.Dispose();

        MediaDetected = null;
        MediaCleared = null;
        RunningPlayersChanged = null;
    }
}
