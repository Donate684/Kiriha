using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Tracking.Sync;

using Kiriha.Core.Domain.Models.Entities;
using Serilog;
using Kiriha.Core.Abstractions.Infrastructure;

namespace Kiriha.Core.Tracking.Core;

public interface IScrobbleService
{
    event EventHandler<string>? CountdownUpdated;
    void StartScrobble(ParsedMedia media, AnimeEntity match);
    void CancelScrobble();
    void UpdatePlayingState(bool isPlaying);
}

public class ScrobbleService : IScrobbleService, IDisposable
{
    private readonly AnimeProgressService _progressService;
    private readonly IHistoryService _historyService;
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILocalizer _localizer;

    public event EventHandler<string>? CountdownUpdated;

    // _stateLock guards _countdownCts and _activeHash. StartScrobble can be invoked
    // concurrently from the Anisthesia background thread and from UI commands.
    private readonly object _stateLock = new();
    private CancellationTokenSource? _countdownCts;
    private string _activeHash = string.Empty;
    private bool _isPlaying;

    public ScrobbleService(
        AnimeProgressService progressService,
        IHistoryService historyService,
        ISettingsService settingsService,
        INotificationService notificationService,
        IBackgroundTaskSupervisor backgroundTasks,
        IUiDispatcher uiDispatcher,
        ILocalizer localizer)
    {
        _progressService = progressService;
        _historyService = historyService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _backgroundTasks = backgroundTasks;
        _uiDispatcher = uiDispatcher;
        _localizer = localizer;
    }

    public void StartScrobble(ParsedMedia media, AnimeEntity match)
    {
        // Try to parse episode number from ParsedMedia. OriginalTitle often contains cleaner number via Anitomy
        var elements = Kiriha.Utils.Parsing.AnimeParseCache.Parse(media.OriginalTitle);
        var epStr = elements.FirstOrDefault(e => e.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeNumber)?.Value ?? media.Episode;

        if (!int.TryParse(epStr, out int ep) || ep <= match.Progress)
        {
            CountdownUpdated?.Invoke(this, _localizer.GetLoc("scrobbler.status.already_scrobbled"));
            return;
        }

        // Check if the detected episode skips ahead beyond the next expected one.
        // For example: progress=5, watching ep 7 Ã¢â‚¬â€ ep 6 was never marked, so updating
        // directly to 7 would skip an episode. When the setting is on, notify and bail.
        if (ep > match.Progress + 1 && _settingsService.Current.System.Scrobbler.NotifyOnSkippedEpisode)
        {
            var msg = string.Format("scrobbler.status.episode_skipped");
            CountdownUpdated?.Invoke(this, msg);
            Log.Information("ScrobbleService: episode skip detected (progress={Progress}, ep={Ep}), notifying instead of updating",
                match.Progress, ep);
            _notificationService.NotifyScrobbleSkipped(match, ep);
            return;
        }

        string hash = $"{match.Id}_{ep}";
        CancellationToken token;
        lock (_stateLock)
        {
            if (_activeHash == hash && _countdownCts != null && !_countdownCts.IsCancellationRequested) return;

            // Cancel & dispose the previous CTS atomically so we don't accumulate
            // un-disposed CancellationTokenSources across episode changes.
            _countdownCts?.Cancel();
            _countdownCts?.Dispose();
            _countdownCts = new CancellationTokenSource();
            _activeHash = hash;
            _isPlaying = media.IsPlaying;
            token = _countdownCts.Token;
        }
        CountdownUpdated?.Invoke(this, string.Empty);

        _backgroundTasks.Run("ScrobbleService.Countdown", ct => CountdownTaskAsync(ep, match, media, ct), token);
    }

    public void UpdatePlayingState(bool isPlaying)
    {
        lock (_stateLock)
        {
            _isPlaying = isPlaying;
        }
    }

    public void CancelScrobble()
    {
        lock (_stateLock)
        {
            _countdownCts?.Cancel();
            _countdownCts?.Dispose();
            _countdownCts = null;
            _activeHash = string.Empty;
        }
        CountdownUpdated?.Invoke(this, string.Empty);
    }

    private async Task CountdownTaskAsync(int targetEp, AnimeEntity match, ParsedMedia media, CancellationToken ct)
    {
        int elapsed = 0;

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool isPlaying;
                lock (_stateLock)
                {
                    isPlaying = _isPlaying;
                }

                if (!isPlaying)
                {
                    CountdownUpdated?.Invoke(this, string.Format("scrobbler.status.paused"));
                    await Task.Delay(1000, ct);
                    continue;
                }

                int delaySeconds = _settingsService.Current.System.Scrobbler.DelaySeconds;
                int remaining = delaySeconds - elapsed;

                if (remaining <= 0) break;

                CountdownUpdated?.Invoke(this, $"{TimeSpan.FromSeconds(remaining):mm\\:ss}");
                await Task.Delay(1000, ct);
                elapsed++;
            }

            ct.ThrowIfCancellationRequested();

            UserAnimeStatus? nextStatus = null;
            if (match.TotalEpisodes > 0 && targetEp >= match.TotalEpisodes)
            {
                nextStatus = UserAnimeStatus.Completed;
            }

            bool alreadyScrobbled = await _uiDispatcher.InvokeAsync(() => match.Progress >= targetEp);
            if (alreadyScrobbled)
            {
                CountdownUpdated?.Invoke(this, _localizer.GetLoc("scrobbler.status.already_scrobbled"));
                return;
            }

            await _progressService.UpdateProgressAsync(match, targetEp, nextStatus);
            _historyService.AddEntry(match.Id, match.Title, match.RussianTitle, targetEp, nextStatus == UserAnimeStatus.Completed ? "Completed" : "Scrobbled");

            CountdownUpdated?.Invoke(this, string.Format("scrobbler.status.updated"));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error(ex, "Scrobble countdown error");
            CountdownUpdated?.Invoke(this, string.Format("common.errors.generic"));
        }
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            _countdownCts?.Cancel();
            _countdownCts?.Dispose();
            _countdownCts = null;
        }
    }
}

