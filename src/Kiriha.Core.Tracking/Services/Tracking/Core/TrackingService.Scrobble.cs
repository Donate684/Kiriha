using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Infrastructure.Player;
using Kiriha.Utils.Async;

namespace Kiriha.Core.Tracking.Core;

public partial class TrackingService
{
    private bool HandleSameMediaUpdate(ParsedMedia prev, ParsedMedia media)
    {
        bool timeLeap = false;
        if (prev.Position.HasValue && media.Position.HasValue)
        {
            timeLeap = Math.Abs((media.Position.Value - prev.Position.Value).TotalSeconds) > 3;
        }

        bool durationChanged = Math.Abs((prev.Duration?.TotalSeconds ?? 0) - (media.Duration?.TotalSeconds ?? 0)) > 1.0;

        bool changed = prev.IsPlaying != media.IsPlaying ||
                       prev.ProcessName != media.ProcessName ||
                       timeLeap ||
                       durationChanged;

        lock (_state) _currentMedia = media;

        _scrobbleService.UpdatePlaybackProgress(media.IsPlaying, media.Position, media.Duration);

        if (!changed)
        {
            return true;
        }

        _uiDispatcher.Post(() => CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new Kiriha.Core.Abstractions.Messages.MediaChangedMessage(media)));
        _scrobbleService.UpdatePlayingState(media.IsPlaying);

        AnimeEntity? currentMatched;
        lock (_state) currentMatched = _matchedAnime;

        if (currentMatched != null)
        {
            UpdateDiscordPresence(media, currentMatched);
        }

        return true;
    }

    private void ApplyMatchedMedia(ParsedMedia media, AnimeEntity? matched)
    {
        bool isValid;
        ParsedMedia actualMedia = media;
        lock (_state)
        {
            isValid = IsSameMedia(_currentMedia, media);
            if (isValid)
            {
                _matchedAnime = matched;
                actualMedia = _currentMedia!;
            }
        }

        if (!isValid) return;

        _uiDispatcher.Post(() => CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new Kiriha.Core.Abstractions.Messages.AnimeMatchedMessage(matched)));

        if (matched != null)
        {
            var effectiveMedia = (matched.TotalEpisodes <= 1 && string.IsNullOrWhiteSpace(actualMedia.Episode))
                ? actualMedia.WithEpisode("1")
                : actualMedia;

            NotifyPlayerMetadata(effectiveMedia, matched);
            UpdateDiscordPresence(effectiveMedia, matched);

            if (_shikiMetadataService != null && (string.IsNullOrEmpty(matched.RussianTitle) || string.IsNullOrEmpty(matched.RussianSynopsis)))
            {
                LocalizeAndNotifyAsync(effectiveMedia, matched).SafeFireAndForget("TrackingService.LocalizeMatchedMedia");
            }

            if (matched.Status != UserAnimeStatus.None)
                _scrobbleService.StartScrobble(effectiveMedia, matched);
        }
        else
        {
            _discordService.UpdatePresence(actualMedia.AnimeTitle, actualMedia.Episode, 0, null, null, actualMedia.Position, actualMedia.Duration, null, actualMedia.IsPlaying);
        }
    }

    private async Task LocalizeAndNotifyAsync(ParsedMedia media, AnimeEntity matched)
    {
        try
        {
            if (_shikiMetadataService != null)
            {
                await _shikiMetadataService.EnsureLocalizedAsync(matched).ConfigureAwait(false);
            }

            ParsedMedia? cur;
            AnimeEntity? curMatched;
            lock (_state)
            {
                cur = _currentMedia;
                curMatched = _matchedAnime;
            }

            if (IsSameMedia(cur, media) && (curMatched == null || curMatched.Id == matched.Id))
            {
                lock (_state)
                {
                    _matchedAnime = matched;
                }
                NotifyPlayerMetadata(media, matched);
                UpdateDiscordPresence(media, matched);
                _uiDispatcher.Post(() => CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(
                    new Kiriha.Core.Abstractions.Messages.AnimeMatchedMessage(matched)));
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
            ParsedMedia? prev;
            AnimeEntity? existingMatched;
            lock (_state)
            {
                prev = _currentMedia;
                existingMatched = _matchedAnime;
            }

            bool sameOriginal = string.Equals(prev?.OriginalTitle, media.OriginalTitle, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(System.IO.Path.GetFileNameWithoutExtension(prev?.OriginalTitle), System.IO.Path.GetFileNameWithoutExtension(media.OriginalTitle), StringComparison.OrdinalIgnoreCase);

            bool isSameMediaFile = prev != null &&
                (sameOriginal || (existingMatched?.Id == animeId && existingMatched.Id > 0)) &&
                AreEpisodesEqual(prev.Episode, media.Episode) &&
                (string.Equals(prev.AnimeTitle, media.AnimeTitle, StringComparison.OrdinalIgnoreCase) || existingMatched?.Id == animeId);

            if (isSameMediaFile)
            {
                if (HandleSameMediaUpdate(prev!, media)) return;
            }

            lock (_state)
            {
                _currentMedia = media;
                _matchedAnime = null;
            }
            _scrobbleService.CancelScrobble();

            _uiDispatcher.Post(() =>
            {
                CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new Kiriha.Core.Abstractions.Messages.MediaChangedMessage(media));
                CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new Kiriha.Core.Abstractions.Messages.AnimeMatchedMessage(null));
            });

            const int repoInitTimeoutMs = 5000;
            try { await Task.WhenAny(_animeRepo.InitializationTask, Task.Delay(repoInitTimeoutMs)); } catch (Exception ex) when (ex is not OperationCanceledException) { }

            var userList = await _uiDispatcher.InvokeAsync(() => System.Linq.Enumerable.ToList(_animeRepo.GetCollection()));
            var matched = System.Linq.Enumerable.FirstOrDefault(userList, x => x.Id == animeId);

            if (matched is null && existingMatched != null && existingMatched.Id == animeId)
            {
                matched = existingMatched;
            }

            if (matched is null)
            {
                var activeTracker = System.Linq.Enumerable.FirstOrDefault(_trackers, t => t.IsEnabled)
                    ?? System.Linq.Enumerable.FirstOrDefault(_trackers.OfType<IMalApiService>());
                if (activeTracker != null)
                {
                    try
                    {
                        var fetched = await activeTracker.GetAnimeDetailsAsync(animeId);
                        if (fetched != null)
                        {
                            matched = fetched.Clone();
                            matched.Status = UserAnimeStatus.None; // Ensure it's not scrobbled or auto-added incorrectly
                        }
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "Failed to fetch anime details for ID {AnimeId}", animeId);
                    }
                }
            }
            else if (matched.IsMissingDetails)
            {
                var activeTracker = System.Linq.Enumerable.FirstOrDefault(_trackers, t => t.IsEnabled)
                    ?? System.Linq.Enumerable.FirstOrDefault(_trackers.OfType<IMalApiService>());
                if (activeTracker != null)
                {
                    try
                    {
                        var fetched = await activeTracker.GetAnimeDetailsAsync(animeId);
                        if (fetched != null)
                        {
                            matched.MergeFullDetails(fetched);
                        }
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "Failed to enrich anime details for ID {AnimeId}", animeId);
                    }
                }
            }

            if (matched != null && existingMatched != null && existingMatched.Id == matched.Id)
            {
                if (string.IsNullOrEmpty(matched.RussianTitle) && !string.IsNullOrEmpty(existingMatched.RussianTitle))
                    matched.RussianTitle = existingMatched.RussianTitle;
                if (string.IsNullOrEmpty(matched.RussianSynopsis) && !string.IsNullOrEmpty(existingMatched.RussianSynopsis))
                    matched.RussianSynopsis = existingMatched.RussianSynopsis;
            }

            ParsedMedia? cur;
            lock (_state) cur = _currentMedia;
            if (!IsSameMedia(cur, media)) return;

            ApplyMatchedMedia(media, matched);
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
        bool sameOriginal = string.Equals(a.OriginalTitle, b.OriginalTitle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(System.IO.Path.GetFileNameWithoutExtension(a.OriginalTitle), System.IO.Path.GetFileNameWithoutExtension(b.OriginalTitle), StringComparison.OrdinalIgnoreCase);
        return sameOriginal &&
               AreEpisodesEqual(a.Episode, b.Episode) &&
               string.Equals(a.AnimeTitle, b.AnimeTitle, StringComparison.OrdinalIgnoreCase);
    }

    public void NotifyCurrentMediaMetadataUpdated(AnimeEntity matched)
    {
        ParsedMedia? media;
        lock (_state)
        {
            _matchedAnime = matched;
            media = _currentMedia;
        }

        if (media != null)
        {
            var effectiveMedia = (matched.TotalEpisodes <= 1 && string.IsNullOrWhiteSpace(media.Episode))
                ? media.WithEpisode("1")
                : media;

            NotifyPlayerMetadata(effectiveMedia, matched);
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
            media.TorrentHash);

        _internalPlayerServer.SendMetadataAsync(metadata).SafeFireAndForget("SendMetadataAsync");

        PlayerProcessBridge.ForwardMetadata(
            media.OriginalTitle,
            matched.Id,
            matched.RussianTitle,
            matched.EnglishTitle,
            matched.Title,
            media.Episode,
            media.TorrentHash);
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
            media.TorrentHash);

        _internalPlayerServer.SendMetadataAsync(metadata).SafeFireAndForget("SendMetadataAsync");

        PlayerProcessBridge.ForwardMetadata(
            media.OriginalTitle,
            0,
            string.Empty,
            string.Empty,
            media.AnimeTitle,
            media.Episode,
            media.TorrentHash);
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
}
