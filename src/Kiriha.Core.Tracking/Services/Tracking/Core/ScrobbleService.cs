using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Sync;
using Serilog;

namespace Kiriha.Core.Tracking.Core;

public interface IScrobbleService
{
    event EventHandler<string>? CountdownUpdated;
    void StartScrobble(ParsedMedia media, AnimeEntity match);
    void CancelScrobble();
    void UpdatePlayingState(bool isPlaying);
    void UpdatePlaybackProgress(bool isPlaying, TimeSpan? position, TimeSpan? duration);
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
    private readonly IScrobbleProgressStore _progressStore;

    public event EventHandler<string>? CountdownUpdated;

    private readonly Lock _stateLock = new();
    private CancellationTokenSource? _countdownCts;
    private string _activeHash = string.Empty;
    private bool _isPlaying;
    private TimeSpan? _currentDuration;
    private int _currentElapsed;

    public ScrobbleService(
        AnimeProgressService progressService,
        IHistoryService historyService,
        ISettingsService settingsService,
        INotificationService notificationService,
        IBackgroundTaskSupervisor backgroundTasks,
        IUiDispatcher uiDispatcher,
        ILocalizer localizer,
        IScrobbleProgressStore? progressStore = null)
    {
        _progressService = progressService;
        _historyService = historyService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _backgroundTasks = backgroundTasks;
        _uiDispatcher = uiDispatcher;
        _localizer = localizer;
        _progressStore = progressStore ?? new ScrobbleProgressStore();
    }

    private const double DefaultEpisodeDurationSeconds = 24 * 60; // 1440s (24 min)

    public void StartScrobble(ParsedMedia media, AnimeEntity match)
    {
        var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(media.OriginalTitle);
        var epStr = !string.IsNullOrEmpty(parsed.Episode) ? parsed.Episode : media.Episode;

        if (!int.TryParse(epStr, out int ep))
        {
            return;
        }

        // Auto-Rewatch candidate: title is Completed, not currently rewatching, and user launched episode 1
        bool isRewatchCandidate = match.Status == UserAnimeStatus.Completed && !match.IsRewatching && ep == 1;

        if (!isRewatchCandidate && ep <= match.Progress)
        {
            CountdownUpdated?.Invoke(this, _localizer.GetLoc("scrobbler.status.already_scrobbled"));
            return;
        }

        if (isRewatchCandidate)
        {
            WeakReferenceMessenger.Default.Send(new AnimeRewatchPromptMessage(match, ep));
            CountdownUpdated?.Invoke(this, _localizer.GetLoc("scrobbler.rewatch_prompt.title"));
            return;
        }

        // Check if the detected episode skips ahead beyond the next expected one.
        if (ep > match.Progress + 1 && _settingsService.Current.System.Scrobbler.NotifyOnSkippedEpisode)
        {
            var msg = _localizer.GetLoc("scrobbler.status.episode_skipped");
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

            bool isHashChanged = !string.IsNullOrEmpty(_activeHash) && _activeHash != hash;

            _countdownCts?.Cancel();
            _countdownCts?.Dispose();
            _countdownCts = new CancellationTokenSource();

            if (media.Duration is { TotalSeconds: > 0 })
            {
                _currentDuration = media.Duration;
            }
            else if (isHashChanged)
            {
                _currentDuration = null;
            }

            _activeHash = hash;
            _isPlaying = media.IsPlaying;
            token = _countdownCts.Token;
        }
        CountdownUpdated?.Invoke(this, string.Empty);

        _backgroundTasks.Run("ScrobbleService.Countdown", ct => CountdownTaskAsync(ep, match, media, ct), token);
    }

    public void UpdatePlayingState(bool isPlaying)
    {
        UpdatePlaybackProgress(isPlaying, null, null);
    }

    public void UpdatePlaybackProgress(bool isPlaying, TimeSpan? position, TimeSpan? duration)
    {
        lock (_stateLock)
        {
            _isPlaying = isPlaying;
            if (duration.HasValue && duration.Value > TimeSpan.Zero) _currentDuration = duration.Value;
        }
    }

    public void CancelScrobble()
    {
        lock (_stateLock)
        {
            if (!string.IsNullOrEmpty(_activeHash) && _currentElapsed > 0)
            {
                _progressStore.SaveElapsed(_activeHash, _currentElapsed);
            }
            _countdownCts?.Cancel();
            _countdownCts?.Dispose();
            _countdownCts = null;
            _activeHash = string.Empty;
            _currentElapsed = 0;
            _currentDuration = null;
        }
        CountdownUpdated?.Invoke(this, string.Empty);
    }

    internal int CalculateTargetSeconds(TimeSpan? currentDur, ParsedMedia media)
    {
        var scrobblerConfig = _settingsService.Current.System.Scrobbler;
        if (scrobblerConfig.Mode == ScrobbleDelayMode.Percentage)
        {
            var dur = currentDur ?? media.Duration;
            double effectiveDurationSeconds = (dur.HasValue && dur.Value.TotalSeconds > 60)
                ? dur.Value.TotalSeconds
                : DefaultEpisodeDurationSeconds;

            int percent = Math.Clamp(scrobblerConfig.DelayPercent, 1, 100);
            return Math.Max(1, (int)Math.Round(effectiveDurationSeconds * percent / 100.0));
        }

        return scrobblerConfig.DelaySeconds <= 0 ? 0 : Math.Max(1, scrobblerConfig.DelaySeconds);
    }

    private async Task CountdownTaskAsync(int targetEp, AnimeEntity match, ParsedMedia media, CancellationToken ct)
    {
        string progressKey = $"{match.Id}_{targetEp}";
        int elapsed = _progressStore.GetElapsed(progressKey);

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool isPlaying;
                TimeSpan? currentDur;
                lock (_stateLock)
                {
                    isPlaying = _isPlaying;
                    currentDur = _currentDuration;
                    _currentElapsed = elapsed;
                }

                if (!isPlaying)
                {
                    CountdownUpdated?.Invoke(this, _localizer.GetLoc("scrobbler.status.paused"));
                    await Task.Delay(1000, ct);
                    continue;
                }

                int targetSeconds = CalculateTargetSeconds(currentDur, media);
                int remaining = targetSeconds - elapsed;

                if (remaining <= 0) break;

                CountdownUpdated?.Invoke(this, $"{TimeSpan.FromSeconds(remaining):mm\\:ss}");
                await Task.Delay(1000, ct);
                elapsed++;

                if (elapsed % 5 == 0)
                {
                    _progressStore.SaveElapsed(progressKey, elapsed);
                }
            }

            ct.ThrowIfCancellationRequested();
            _progressStore.Clear(progressKey);
            lock (_stateLock)
            {
                _currentElapsed = 0;
            }

            UserAnimeStatus? nextStatus = null;
            if (match.TotalEpisodes > 0 && targetEp >= match.TotalEpisodes)
            {
                nextStatus = UserAnimeStatus.Completed;
            }
            else if (match.Status == UserAnimeStatus.PlanToWatch ||
                     match.Status == UserAnimeStatus.OnHold ||
                     match.Status == UserAnimeStatus.Dropped ||
                     match.Status == UserAnimeStatus.None)
            {
                // Auto-Start: user started watching an anime that was in PlanToWatch, OnHold, Dropped or not in list
                nextStatus = UserAnimeStatus.Watching;
            }

            bool alreadyScrobbled = await _uiDispatcher.InvokeAsync(() => match.Progress >= targetEp);
            if (alreadyScrobbled)
            {
                CountdownUpdated?.Invoke(this, _localizer.GetLoc("scrobbler.status.already_scrobbled"));
                return;
            }

            // Write history BEFORE UpdateProgressAsync (which enqueues sync / fires SetPendingTrackers)
            // so the DB row exists when SetPendingTrackers tries to mark it as Pending.
            await _historyService.AddEntryAsync(match.Id, match.Title, match.RussianTitle, targetEp,
                nextStatus == UserAnimeStatus.Completed ? "Completed" : "Scrobbled", null, match.MainPictureUrl);
            await _progressService.UpdateProgressAsync(match, targetEp, nextStatus);

            // Auto-Complete & Quick Rating notification
            if (nextStatus == UserAnimeStatus.Completed)
            {
                WeakReferenceMessenger.Default.Send(new AnimeCompletedRatingPromptMessage(match));
                _notificationService.NotifyAnimeCompleted(match);
            }

            if (nextStatus.HasValue)
            {
                WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
            }

            CountdownUpdated?.Invoke(this, _localizer.GetLoc("scrobbler.status.updated"));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error(ex, "Scrobble countdown error");
            CountdownUpdated?.Invoke(this, _localizer.GetLoc("common.errors.generic"));
        }
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (!string.IsNullOrEmpty(_activeHash) && _currentElapsed > 0)
            {
                _progressStore.SaveElapsed(_activeHash, _currentElapsed);
            }
            _countdownCts?.Cancel();
            _countdownCts?.Dispose();
            _countdownCts = null;
        }
    }
}