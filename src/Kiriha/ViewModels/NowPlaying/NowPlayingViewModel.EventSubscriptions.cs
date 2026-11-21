using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.ViewModels.NowPlaying;

public partial class NowPlayingViewModel
{
    public void Receive(TrackingStatusMessage message)
    {
        TrackingStatus = string.IsNullOrEmpty(message.Status) ? string.Empty : _localizer.GetLoc(message.Status);
    }

    public void Receive(TrackingCountdownMessage message)
    {
        CountdownStatus = message.Countdown;
    }

    public void Receive(AnimeMatchedMessage message)
    {
        var anime = message.Anime;
        // Suppress intermediate events while a manual selection is in flight.
        // - null: a transient "clearing previous match" event before MappingService re-resolves
        // - different id: stale background match for a previous media — would clobber UI choice
        // We let through the matching id so we can clear the pending guard below.
        var pending = Volatile.Read(ref _pendingManualMatchId);
        if (pending != 0 && (anime is null || anime.Id != pending)) return;
        if (pending != 0 && anime != null && anime.Id == pending)
        {
            Interlocked.CompareExchange(ref _pendingManualMatchId, 0, pending);
        }

        MatchedAnime = anime;
        OnPropertyChanged(nameof(CurrentMedia));
        OnPropertyChanged(nameof(DisplayEpisodeNumber));
        if (anime != null)
        {
            IsManuallyMapped = _trackingService.IsManuallyMapped();
            LogDetection(CurrentMedia ?? new ParsedMedia { AnimeTitle = anime.Title }, _localizer.GetLoc("scrobbler.status.matched"));

        }
        else
        {
            IsManuallyMapped = false;
        }
    }

    public void Receive(MediaChangedMessage message)
    {
        var media = message.Media;
        // Drop the manual-selection guard only when the media actually changes
        // (different file or playback stopped). ManualMapAsync re-runs
        // MatchMediaAsync with the *same* media to force a re-match, which
        // re-fires MediaChanged; clearing the guard there would let the
        // intermediate AnimeMatched(null) event clobber the user's choice.
        var prev = CurrentMedia;
        bool sameFile = media != null && prev != null
            && prev.HasSamePlaybackSource(media) && prev.HasSameSourceFile(media);
        if (!sameFile) Volatile.Write(ref _pendingManualMatchId, 0);

        CurrentMedia = media;
        IsMediaDetected = media != null;
        Suggestions.Clear();
        ShowSuggestions = false;
        SearchQuery = string.Empty;
        IsSearchPanelOpen = false;
        TrackingStatus = string.Empty;
        OnPropertyChanged(nameof(HasSuggestions));
        if (media != null)
        {
            IsPaused = !media.IsPlaying;
            LogDetection(media, _localizer.GetLoc("scrobbler.status.detected"));
        }
        else
        {
            MatchedAnime = null;
            IsManuallyMapped = false;
            CountdownStatus = string.Empty;
        }
    }

    private Task EnsureLocalizedSafeAsync(AnimeEntity anime) =>
        EnsureLocalizedSafeAsync(anime, new MediaKey(anime.Id, anime.MediaKind),
            _animeRepo.GetEntryGeneration(anime.Id, anime.MediaKind), Volatile.Read(ref _matchRevision), _disposeCts.Token);

    private async Task EnsureLocalizedSafeAsync(AnimeEntity anime, MediaKey key, long generation, long revision, CancellationToken ct)
    {
        try
        {
            await _shikiMetadataService.EnsureLocalizedAsync(anime, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "NowPlaying: EnsureLocalizedAsync failed for {Id}", anime.Id);
        }

        if (!IsCurrentMatch(anime, key, generation, revision, ct)) return;

        UpdateAlternativeTitles(anime);
        UpdateCustomShareLinks(anime);
        anime.RefreshMetadata();
        var current = _animeRepo.FindById(key.Id, key.Kind);
        if (current != null || anime.Status == UserAnimeStatus.None)
            _trackingService.NotifyCurrentMediaMetadataUpdated(current ?? anime);

        OnPropertyChanged(nameof(MatchedAnime));
        OnPropertyChanged(nameof(DisplayEpisodeNumber));
        OnPropertyChanged(nameof(IsNotInList));
    }

    private void LogDetection(ParsedMedia media, string status)
    {
        string?[] extrasParts = [media.VideoResolution, media.Source, media.AnimeType];
        string extras = string.Join(" ", extrasParts.Where(s => !string.IsNullOrEmpty(s)));
        string extraInfo = !string.IsNullOrEmpty(extras) ? $" [{extras}]" : "";
        string epInfo = !string.IsNullOrEmpty(media.Episode) ? $" ({_localizer.GetLoc("anime.labels.episode")} {media.Episode})" : "";
        string logEntry = $"[{DateTime.Now:HH:mm:ss}] {status}: {media.AnimeTitle}{epInfo}{extraInfo}";
        DetectionLogs.Insert(0, logEntry);
        if (DetectionLogs.Count > 50) DetectionLogs.RemoveAt(50);
    }
}
