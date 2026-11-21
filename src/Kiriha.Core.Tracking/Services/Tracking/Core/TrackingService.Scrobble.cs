using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Utils.Async;

namespace Kiriha.Core.Tracking.Core;

public partial class TrackingService
{
    // Called under _state by both detection paths.
    private void HandleSameMediaUpdate(ParsedMedia prev, ParsedMedia media)
    {
        bool timeLeap = prev.Position.HasValue && media.Position.HasValue &&
                        Math.Abs((media.Position.Value - prev.Position.Value).TotalSeconds) > 3;
        bool durationChanged = Math.Abs((prev.Duration?.TotalSeconds ?? 0) - (media.Duration?.TotalSeconds ?? 0)) > 1.0;
        bool changed = prev.IsPlaying != media.IsPlaying || timeLeap || durationChanged;

        _currentMedia = media;
        _scrobbleService.UpdatePlaybackProgress(media.IsPlaying, media.Position, media.Duration);
        if (!changed)
            return;

        PostForMedia(_mediaVersion, () => _eventBus.Publish(new MediaChangedMessage(media)));
        _scrobbleService.UpdatePlayingState(media.IsPlaying);
        if (_matchedAnime is { } currentMatched)
            UpdateDiscordPresence(media, currentMatched);
    }

    private void ApplyMatchedMedia(long version, AnimeEntity? matched, AnimeEntity? details = null, AnimeEntity? previousMatch = null)
    {
        lock (_state)
        {
            if (!IsCurrentMediaVersion(version) || _currentMedia is not { } actualMedia)
                return;

            if (matched != null && details != null && matched.Id == details.Id && matched.MediaKind == details.MediaKind)
                matched.MergeFullDetails(details, metadataOnly: true);
            if (matched != null && previousMatch != null && matched.Id == previousMatch.Id && matched.MediaKind == previousMatch.MediaKind)
            {
                if (string.IsNullOrEmpty(matched.RussianTitle))
                    matched.RussianTitle = previousMatch.RussianTitle;
                if (string.IsNullOrEmpty(matched.RussianSynopsis))
                    matched.RussianSynopsis = previousMatch.RussianSynopsis;
            }

            _matchedAnime = matched;
            PostForMedia(version, () => _eventBus.Publish(new AnimeMatchedMessage(matched)));
            if (matched is null)
            {
                _discordService.UpdatePresence(actualMedia.AnimeTitle, actualMedia.Episode, 0, null, null,
                    actualMedia.Position, actualMedia.Duration, null, actualMedia.IsPlaying);
                return;
            }

            var effectiveMedia = GetEffectiveMedia(actualMedia, matched);
            NotifyPlayerMetadata(effectiveMedia, matched);
            UpdateDiscordPresence(effectiveMedia, matched);
            if (matched.Status != UserAnimeStatus.None && _settingsService.Current.System.Scrobbler.Enabled)
                _scrobbleService.StartScrobble(effectiveMedia, matched);
        }

        if (_shikiMetadataService != null &&
            (string.IsNullOrEmpty(matched.RussianTitle) || string.IsNullOrEmpty(matched.RussianSynopsis)))
        {
            LocalizeAndNotifyAsync(version, matched).SafeFireAndForget("TrackingService.LocalizeMatchedMedia");
        }
    }

    private async Task LocalizeAndNotifyAsync(long version, AnimeEntity matched)
    {
        try
        {
            if (_shikiMetadataService != null)
                await _shikiMetadataService.EnsureLocalizedAsync(matched).ConfigureAwait(false);

            lock (_state)
            {
                if (!IsCurrentMediaVersion(version) || !ReferenceEquals(_matchedAnime, matched) || _currentMedia is not { } current)
                    return;

                var effectiveMedia = GetEffectiveMedia(current, matched);
                NotifyPlayerMetadata(effectiveMedia, matched);
                UpdateDiscordPresence(effectiveMedia, matched);
                PostForMedia(version, () => _eventBus.Publish(new AnimeMatchedMessage(matched)));
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "TrackingService: Failed to localize matched anime {AnimeId}", matched.Id);
        }
    }

    private async Task SetMatchedInternalMedia(ParsedMedia media, int animeId)
    {
        try
        {
            AnimeEntity? existingMatched;
            long version;
            lock (_state)
            {
                if (_disposed != 0) return;

                var previous = _currentMedia;
                existingMatched = _matchedAnime;
                bool sameAnime = existingMatched?.Id == animeId;
                if (previous != null && (existingMatched is null || sameAnime) &&
                    previous.HasSamePlaybackSource(media) && previous.HasSameSourceFile(media) &&
                    AreEpisodesEqual(previous.Episode, media.Episode))
                {
                    HandleSameMediaUpdate(previous, media);
                    return;
                }

                version = BeginMedia(media, showMatchingStatus: false);
            }

            const int repoInitTimeoutMs = 5000;
            try { await Task.WhenAny(_animeRepo.InitializationTask, Task.Delay(repoInitTimeoutMs)); } catch (Exception ex) when (ex is not OperationCanceledException) { }

            var matched = _animeRepo.FindById(animeId, Kiriha.Core.Domain.Models.Entities.MediaKind.Anime);
            if (matched is null)
            {
                var userList = await _uiDispatcher.InvokeAsync(() => System.Linq.Enumerable.ToList(_animeRepo.GetCollection()));
                if (userList != null)
                {
                    matched = System.Linq.Enumerable.FirstOrDefault(userList, x => x.Id == animeId && x.MediaKind == Kiriha.Core.Domain.Models.Entities.MediaKind.Anime);
                }
            }

            if (matched is null && existingMatched != null && existingMatched.Id == animeId && existingMatched.MediaKind == MediaKind.Anime)
            {
                matched = existingMatched;
            }

            AnimeEntity? details = null;
            if (matched is null)
            {
                var fetched = await FetchDetailsFromTrackersAsync(animeId);
                if (fetched != null)
                {
                    matched = fetched.Clone();
                    matched.Status = UserAnimeStatus.None; // Ensure it's not scrobbled or auto-added incorrectly
                }
            }
            else if (matched.IsMissingDetails || (matched.MediaKind == MediaKind.Anime && matched.TotalEpisodes <= 0))
            {
                details = await FetchDetailsFromTrackersAsync(animeId);
            }

            ApplyMatchedMedia(version, matched, details, existingMatched);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error during tracking mapping for internal player");
        }
    }

    private static bool AreEpisodesEqual(string? ep1, string? ep2)
    {
        if (string.Equals(ep1, ep2, StringComparison.OrdinalIgnoreCase)) return true;
        if (int.TryParse(ep1, out int n1) && int.TryParse(ep2, out int n2)) return n1 == n2;
        if (string.IsNullOrWhiteSpace(ep1) && string.IsNullOrWhiteSpace(ep2)) return true;
        return false;
    }

    private static bool IsSameMedia(ParsedMedia? a, ParsedMedia? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        return a.HasSamePlaybackSource(b) && a.HasSameSourceFile(b) && AreEpisodesEqual(a.Episode, b.Episode) &&
            (!string.IsNullOrEmpty(a.PlaybackSourceKey) && !string.IsNullOrEmpty(b.PlaybackSourceKey) ||
                string.Equals(a.AnimeTitle, b.AnimeTitle, StringComparison.OrdinalIgnoreCase));
    }

    private static ParsedMedia GetEffectiveMedia(ParsedMedia media, AnimeEntity anime) =>
        anime.TotalEpisodes <= 1 && string.IsNullOrWhiteSpace(media.Episode) ? media.WithEpisode("1") : media;

    public void NotifyCurrentMediaMetadataUpdated(AnimeEntity anime)
    {
        lock (_state)
        {
            if (_disposed != 0 || _currentMedia is not { } media ||
                _matchedAnime?.Id != anime.Id || _matchedAnime.MediaKind != anime.MediaKind)
                return;

            _matchedAnime = anime;
            var effectiveMedia = GetEffectiveMedia(media, anime);
            NotifyPlayerMetadata(effectiveMedia, anime);
            UpdateDiscordPresence(effectiveMedia, anime);

            if (anime.Status != UserAnimeStatus.None && _settingsService.Current.System.Scrobbler.Enabled)
                _scrobbleService.StartScrobble(effectiveMedia, anime);
        }
    }

    private void NotifyPlayerMetadata(ParsedMedia media, AnimeEntity matched)
    {
        if (!string.Equals(media.ProcessName, "KirihaInternal", StringComparison.Ordinal))
            return;

        var metadata = new PlayerMediaMetadata(
            media.OriginalTitle,
            matched.RussianTitle ?? string.Empty,
            matched.EnglishTitle ?? string.Empty,
            media.Episode ?? string.Empty,
            matched.Id,
            matched.Title ?? string.Empty,
            media.TorrentHash)
        {
            PlaybackRequestId = media.PlaybackRequestId,
            PlaybackSourceKey = media.PlaybackSourceKey
        };

        _internalPlayerServer.SendMetadataAsync(metadata).SafeFireAndForget("SendMetadataAsync");
    }

    private void ClearPlayerMetadata(ParsedMedia media)
    {
        if (!string.Equals(media.ProcessName, "KirihaInternal", StringComparison.Ordinal))
            return;

        var metadata = new PlayerMediaMetadata(
            media.OriginalTitle,
            string.Empty,
            string.Empty,
            media.Episode ?? string.Empty,
            null,
            media.AnimeTitle ?? string.Empty,
            media.TorrentHash)
        {
            PlaybackRequestId = media.PlaybackRequestId,
            PlaybackSourceKey = media.PlaybackSourceKey
        };

        _internalPlayerServer.SendMetadataAsync(metadata).SafeFireAndForget("SendMetadataAsync");
    }

    private void UpdateDiscordPresence(ParsedMedia media, AnimeEntity matched)
    {
        string? mainTitle = matched.Title ?? matched.EnglishTitle;
        string? subTitle = matched.RussianTitle;

        string discordTitle = (!string.IsNullOrEmpty(subTitle) && !string.IsNullOrEmpty(mainTitle) && subTitle != mainTitle)
            ? $"{mainTitle} | {subTitle}"
            : (!string.IsNullOrEmpty(subTitle) ? subTitle : (mainTitle ?? "Anime"));

        string malUrl = $"{Kiriha.Core.Domain.Constants.AppConstants.Api.Mal.WebsiteUrl}{matched.Id}";
        string shikiUrl = $"{ShikiEndpoints.WebsiteUrl(_settingsService.Current.Api.ShikiMirror)}{matched.Id}";

        _discordService.UpdatePresence(discordTitle, media.Episode, matched.TotalEpisodes, malUrl, shikiUrl, media.Position, media.Duration, matched.MainPictureUrl, media.IsPlaying);
    }

    private async Task<AnimeEntity?> FetchDetailsFromTrackersAsync(int animeId)
    {
        foreach (var tracker in System.Linq.Enumerable.Concat(
            System.Linq.Enumerable.Where(_trackers, t => t.IsEnabled),
            System.Linq.Enumerable.Where(_trackers, t => !t.IsEnabled)))
        {
            try
            {
                var fetched = await tracker.GetAnimeDetailsAsync(animeId);
                if (fetched != null)
                    return fetched;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to fetch anime details for ID {AnimeId} via {Tracker}", animeId, tracker.TrackerId);
            }
        }
        return null;
    }
}
