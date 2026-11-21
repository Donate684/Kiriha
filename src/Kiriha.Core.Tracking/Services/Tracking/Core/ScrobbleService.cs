using Kiriha.Core.Domain.Utils.Parsing;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
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

public partial class ScrobbleService : IScrobbleService, IDisposable
{
    private readonly IProgressUpdateService _progressService;
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILocalizer _localizer;
    private readonly IScrobbleProgressStore _progressStore;
    private readonly ILibraryMutationService _mutations;
    private readonly ICoreEventBus _eventBus;
    private readonly IAnimeRepository? _library;
    private readonly TimeProvider _clock;

    public event EventHandler<string>? CountdownUpdated;

    private readonly Lock _stateLock = new();
    private readonly Dictionary<string, long> _checkpointGenerations = new(StringComparer.Ordinal);
    private Countdown? _active;
    private bool _disposed;

    public ScrobbleService(
        IProgressUpdateService progressService,
        ISettingsService settingsService,
        INotificationService notificationService,
        IBackgroundTaskSupervisor backgroundTasks,
        IUiDispatcher uiDispatcher,
        ILocalizer localizer,
        IScrobbleProgressStore? progressStore = null,
        ILibraryMutationService? mutations = null,
        ICoreEventBus? eventBus = null,
        IAnimeRepository? library = null,
        TimeProvider? clock = null)
    {
        _progressService = progressService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _backgroundTasks = backgroundTasks;
        _uiDispatcher = uiDispatcher;
        _localizer = localizer;
        _progressStore = progressStore ?? new InMemoryScrobbleProgressStore();
        _eventBus = eventBus ?? CoreEventBus.Default;
        _mutations = mutations ?? new LibraryMutationService(null, null, null, uiDispatcher, _eventBus);
        _library = library;
        _clock = clock ?? TimeProvider.System;
    }

    private sealed class InMemoryScrobbleProgressStore : IScrobbleProgressStore
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _entries = new(StringComparer.Ordinal);
        public int GetElapsed(string key) => !string.IsNullOrWhiteSpace(key) && _entries.TryGetValue(key, out var val) ? Math.Max(0, val) : 0;
        public void SaveElapsed(string key, int elapsed) { if (!string.IsNullOrWhiteSpace(key)) _entries[key] = Math.Max(0, elapsed); }
        public void Clear(string key) { if (!string.IsNullOrWhiteSpace(key)) _entries.TryRemove(key, out _); }
    }

    private const double DefaultEpisodeDurationSeconds = 24 * 60; // 1440s (24 min)
    private const string AlreadyScrobbledLocKey = "scrobbler.status.already_scrobbled";

    public void StartScrobble(ParsedMedia media, AnimeEntity match)
    {
        if (!_settingsService.Current.System.Scrobbler.Enabled) return;

        if (!int.TryParse(media.Episode, out int ep))
        {
            var fallbackEp = AnimeParseCache.Parse(media.OriginalTitle).Episode;
            if (!int.TryParse(fallbackEp, out ep))
            {
                return;
            }
        }

        if (ep == 0 && match.TotalEpisodes == 1)
        {
            ep = 1;
        }

        var key = new MediaKey(match.Id, match.MediaKind);
        var generation = _library?.GetEntryGeneration(key.Id, key.Kind) ?? 0;
        if (_library != null)
        {
            if (key.Kind != MediaKind.Anime ||
                _library.FindById(key.Id, key.Kind) is not { Status: not UserAnimeStatus.None } current)
                return;

            match = current;
        }

        // Auto-Rewatch candidate: title is Completed, not currently rewatching, and user launched episode 1.
        bool isRewatchCandidate = match.Status == UserAnimeStatus.Completed && !match.IsRewatching && ep == 1;
        bool skipped = ep > match.Progress + 1 && _settingsService.Current.System.Scrobbler.NotifyOnSkippedEpisode;
        Countdown? countdown = null;
        lock (_stateLock)
        {
            if (_disposed || (_active is { } active && active.Episode == ep && active.Key == key &&
                active.Generation == generation && active.Media.HasSamePlaybackSource(media) && active.Media.HasSameSourceFile(media)))
                return;

            StopCountdown();
            if (!isRewatchCandidate && ep > match.Progress && !skipped)
            {
                countdown = new Countdown(media with { }, match, key, generation, ep, _clock.GetTimestamp());
                if (_checkpointGenerations.TryGetValue(countdown.ProgressKey, out var previousGeneration) &&
                    previousGeneration != generation)
                    ClearCheckpoint(countdown);

                _active = countdown;
            }
        }

        if (!isRewatchCandidate && ep <= match.Progress)
        {
            CountdownUpdated?.Invoke(this, _localizer.GetLoc(AlreadyScrobbledLocKey));
            return;
        }

        if (isRewatchCandidate)
        {
            _eventBus.Publish(new AnimeRewatchPromptMessage(match, ep)
            {
                Target = _library == null ? null : new LibraryEntryTarget(key, generation)
            });
            CountdownUpdated?.Invoke(this, _localizer.GetLoc("scrobbler.rewatch_prompt.title"));
            return;
        }

        if (skipped)
        {
            var msg = _localizer.GetLoc("scrobbler.status.episode_skipped");
            CountdownUpdated?.Invoke(this, msg);
            Log.Information("ScrobbleService: episode skip detected (progress={Progress}, ep={Ep}), notifying instead of updating",
                match.Progress, ep);
            _notificationService.NotifyScrobbleSkipped(match, ep);
            return;
        }

        if (countdown is null) return;
        CountdownUpdated?.Invoke(this, string.Empty);
        _ = _backgroundTasks.Run("ScrobbleService.Countdown", ct => CountdownTaskAsync(countdown, ct), countdown.Token);
    }

    public void UpdatePlayingState(bool isPlaying)
    {
        UpdatePlaybackProgress(isPlaying, null, null);
    }

    public void UpdatePlaybackProgress(bool isPlaying, TimeSpan? position, TimeSpan? duration)
    {
        lock (_stateLock)
        {
            if (_active is not { } countdown) return;

            if (isPlaying && countdown.PlayingSince is null)
                countdown.PlayingSince = _clock.GetTimestamp();
            else if (!isPlaying && countdown.PlayingSince.HasValue)
            {
                countdown.Elapsed = ReadElapsed(countdown);
                countdown.PlayingSince = null;
            }

            if (duration is { TotalSeconds: > 0 })
                countdown.Duration = duration;
        }
    }

    public void CancelScrobble()
    {
        lock (_stateLock)
            StopCountdown();
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

    public void Dispose()
    {
        lock (_stateLock)
        {
            _disposed = true;
            StopCountdown();
        }
    }
}
