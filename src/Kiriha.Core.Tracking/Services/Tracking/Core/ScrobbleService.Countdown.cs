using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Core;

public partial class ScrobbleService
{
    private sealed class Countdown
    {
        internal ParsedMedia Media { get; }
        internal AnimeEntity Match { get; }
        internal MediaKey Key { get; }
        internal long Generation { get; }
        internal int Episode { get; }
        internal string ProgressKey { get; }
        internal CancellationTokenSource Cancellation { get; } = new();
        internal CancellationToken Token { get; }
        internal TimeSpan Elapsed { get; set; }
        internal long? PlayingSince { get; set; }
        internal TimeSpan? Duration { get; set; }
        internal int Checkpoint { get; set; }
        internal bool Restored { get; set; }

        internal Countdown(ParsedMedia media, AnimeEntity match, MediaKey key, long generation, int episode, long startedAt)
        {
            Media = media;
            Match = match;
            Key = key;
            Generation = generation;
            Episode = episode;
            ProgressKey = CreateProgressKey(media, key, episode);
            PlayingSince = media.IsPlaying ? startedAt : null;
            Duration = media.Duration is { TotalSeconds: > 0 } ? media.Duration : null;
            Token = Cancellation.Token;
        }
    }

    private static string CreateProgressKey(ParsedMedia media, MediaKey key, int episode)
    {
        var source = media.PlaybackSourceKey ?? media.OriginalTitle.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(source))
            source = media.PlaybackRequestId ?? string.Empty;
        if (string.IsNullOrEmpty(source))
            return $"{key.Id}_{episode}";

        var sourceKey = ParsedMedia.CreatePlaybackSourceKey($"{media.ProcessName.ToUpperInvariant()}|{media.TorrentHash.ToUpperInvariant()}|{source}");
        return $"{key.Kind}_{key.Id}_{episode}_{sourceKey}";
    }

    private TimeSpan ReadElapsed(Countdown countdown) => countdown.PlayingSince is { } since
        ? countdown.Elapsed + _clock.GetElapsedTime(since)
        : countdown.Elapsed;

    private bool IsSameEntry(Countdown countdown) => _library == null ||
        _library.GetEntryGeneration(countdown.Key.Id, countdown.Key.Kind) == countdown.Generation;

    // Call under _stateLock. Only the current countdown may write its checkpoint.
    private void StopCountdown()
    {
        if (_active is not { } countdown) return;

        _active = null;
        countdown.Cancellation.Cancel();
        countdown.Cancellation.Dispose();
        if (!IsSameEntry(countdown) || countdown.Match.Progress >= countdown.Episode)
            ClearCheckpoint(countdown);
        else if (countdown.Restored)
            SaveCheckpoint(countdown, (int)ReadElapsed(countdown).TotalSeconds);
    }

    private void SaveCheckpoint(Countdown countdown, int elapsed)
    {
        if (elapsed <= countdown.Checkpoint) return;
        _progressStore.SaveElapsed(countdown.ProgressKey, elapsed);
        _checkpointGenerations[countdown.ProgressKey] = countdown.Generation;
        countdown.Checkpoint = elapsed;
    }

    private void ClearCheckpoint(Countdown countdown)
    {
        _progressStore.Clear(countdown.ProgressKey);
        _checkpointGenerations.Remove(countdown.ProgressKey);
    }

    private async Task CountdownTaskAsync(Countdown countdown, CancellationToken ct)
    {
        bool clearCheckpoint = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            int restored = Math.Max(0, _progressStore.GetElapsed(countdown.ProgressKey));
            lock (_stateLock)
            {
                if (!ReferenceEquals(_active, countdown)) return;
                countdown.Elapsed += TimeSpan.FromSeconds(restored);
                countdown.Checkpoint = restored;
                countdown.Restored = true;
                if (restored > 0)
                    _checkpointGenerations[countdown.ProgressKey] = countdown.Generation;
            }

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                string text;
                lock (_stateLock)
                {
                    if (!ReferenceEquals(_active, countdown)) return;
                    if (!IsSameEntry(countdown))
                    {
                        StopCountdown();
                        return;
                    }

                    int elapsed = (int)ReadElapsed(countdown).TotalSeconds;
                    int remaining = CalculateTargetSeconds(countdown.Duration, countdown.Media) - elapsed;
                    if (countdown.PlayingSince.HasValue && remaining <= 0) break;
                    if (elapsed - countdown.Checkpoint >= 5)
                        SaveCheckpoint(countdown, elapsed);

                    text = countdown.PlayingSince.HasValue
                        ? $"{TimeSpan.FromSeconds(remaining):mm\\:ss}"
                        : _localizer.GetLoc("scrobbler.status.paused");
                }

                PublishCountdown(countdown, text);
                await Task.Delay(TimeSpan.FromSeconds(1), _clock, ct);
            }

            ct.ThrowIfCancellationRequested();
            var result = await SaveScrobbleAsync(countdown, ct);
            clearCheckpoint = result.Outcome != ScrobbleOutcome.Skipped;

            if (result.Outcome == ScrobbleOutcome.EntryChanged)
                PublishCountdown(countdown, string.Empty);
            else if (result.Outcome == ScrobbleOutcome.AlreadyWatched)
                PublishCountdown(countdown, _localizer.GetLoc(AlreadyScrobbledLocKey));
            else if (result.Outcome == ScrobbleOutcome.Saved && !countdown.Token.IsCancellationRequested)
                NotifySaved(countdown, result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || countdown.Token.IsCancellationRequested)
        {
            // Playback changed or tracking shut down.
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Scrobble countdown error");
            PublishCountdown(countdown, _localizer.GetLoc("common.errors.generic"));
        }
        finally
        {
            lock (_stateLock)
            {
                if (ReferenceEquals(_active, countdown))
                {
                    _active = null;
                    countdown.Cancellation.Dispose();
                    if (clearCheckpoint || !IsSameEntry(countdown))
                        ClearCheckpoint(countdown);
                    else if (countdown.Restored)
                        SaveCheckpoint(countdown, (int)ReadElapsed(countdown).TotalSeconds);
                }
            }
        }
    }

    private async Task<ScrobbleResult> SaveScrobbleAsync(Countdown countdown, CancellationToken ct)
    {
        if (_library != null)
            return await _progressService.RecordScrobbleAsync(countdown.Key, countdown.Generation, countdown.Episode, ct);

        var match = countdown.Match;
        var episode = countdown.Episode;
        var nextStatus = _mutations.ComputeSmartNextStatus(match, episode);
        bool alreadyScrobbled = await _uiDispatcher.InvokeAsync(() => match.Progress >= episode);
        ct.ThrowIfCancellationRequested();
        if (alreadyScrobbled) return new ScrobbleResult(ScrobbleOutcome.AlreadyWatched);
        return await _progressService.RecordScrobbleAsync(match, episode, nextStatus, ct)
            ? new ScrobbleResult(ScrobbleOutcome.Saved, match, nextStatus)
            : default;
    }

    private void PublishCountdown(Countdown countdown, string text)
    {
        if (!countdown.Token.IsCancellationRequested)
            CountdownUpdated?.Invoke(this, text);
    }

    private void NotifySaved(Countdown countdown, ScrobbleResult result)
    {
        if (result.NextStatus == UserAnimeStatus.Completed && result.Entry is { } match)
        {
            _eventBus.Publish(new AnimeCompletedRatingPromptMessage(match)
            {
                Target = _library == null ? null : new LibraryEntryTarget(countdown.Key, countdown.Generation)
            });
            _notificationService.NotifyAnimeCompleted(match);
        }

        if (result.NextStatus.HasValue)
            _eventBus.Publish(new AnimeListRefreshMessage());

        PublishCountdown(countdown, _localizer.GetLoc("scrobbler.status.updated"));
    }
}
