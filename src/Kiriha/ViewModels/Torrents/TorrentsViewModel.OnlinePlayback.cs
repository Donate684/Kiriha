using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Streaming.Services;
using Kiriha.Models;
using Kiriha.Utils;
using Serilog;

namespace Kiriha.ViewModels.Torrents;

public partial class TorrentsViewModel
{
    private AnimeEntity? _activeMatchedAnime;
    private bool _selectedEpisodeWasWatchedWhenSelected;
    private CancellationToken? _onlineLaunchToken;

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task PlayOnlineSourceAsync(OnlineSourceItem? sourceItem)
    {
        if (sourceItem == null || _streamingService == null || ActiveOnlineAnime == null ||
            SelectedOnlineEpisode == null || !OnlineSources.Contains(sourceItem)) return;

        var animeTitle = ActiveOnlineAnime.Title;
        var epTitle = SelectedOnlineEpisode?.Title ?? string.Empty;
        var epNum = SelectedOnlineEpisode?.EpisodeNumber.ToString();

        IsPlayerLaunching = true;
        PlayerLaunchingTitle = $"{animeTitle} - {epTitle} ({sourceItem.Title})";
        OnlineEpisodeSelectorError = null;

        var ct = BeginPlayerLaunch();
        _onlineLaunchToken = ct;

        try
        {
            var videos = await _streamingService.ResolveStreamAsync(sourceItem.PlayerUrl, ct);
            ct.ThrowIfCancellationRequested();
            var validVideos = videos.Where(StreamingMediaUrl.IsPlayable);

            var preferDirect = _settingsService?.Current.Torrents.OnlineStreaming.PreferDirectMp4 ?? false;
            var targetVideo = validVideos
                .OrderByDescending(v => preferDirect && (string.Equals(v.VideoType, "mp4", StringComparison.OrdinalIgnoreCase) || v.Url.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)))
                .ThenByDescending(v => v.Quality)
                .FirstOrDefault();

            string? streamUrl = null;
            if (targetVideo != null)
            {
                streamUrl = StreamingMediaUrl.Normalize(targetVideo.Url);
            }
            else if (IsDirectMediaUrl(sourceItem.PlayerUrl))
            {
                streamUrl = StreamingMediaUrl.Normalize(sourceItem.PlayerUrl);
            }

            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                throw new InvalidOperationException(_localizer.GetLoc("torrents.online.stream_not_found"));
            }

            var subtitleUrl = targetVideo?.SubtitleUrl ?? sourceItem.SubtitleUrl;
            var headers = targetVideo?.Headers;

            var matchedAnime = ResolveMatchedAnime(animeTitle);
            var titleRu = matchedAnime?.RussianTitle ?? animeTitle;
            var titleEn = matchedAnime?.Title ?? matchedAnime?.EnglishTitle ?? animeTitle;
            var originalTitle = !string.IsNullOrWhiteSpace(epNum)
                ? $"{titleEn} - {epNum}"
                : titleEn;

            var launched = await PlayerProcessHelper.LaunchPlayerAsync(
                videoUrl: streamUrl,
                titleRu: titleRu,
                titleEn: titleEn,
                episode: epNum,
                animeId: matchedAnime?.Id,
                originalTitle: originalTitle,
                subtitleUrl: subtitleUrl,
                headers: headers,
                ct: ct);
            if (!launched && !ct.IsCancellationRequested)
                OnlineEpisodeSelectorError = _localizer.GetLoc(AppConstants.Player.OpenFailedLocKey);
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Torrents.Online] Failed to resolve video for '{Url}'", Kiriha.Core.Streaming.Helpers.UrlRedactor.Redact(sourceItem.PlayerUrl));
            Dispatcher.UIThread.Post(() =>
            {
                if (!ct.IsCancellationRequested)
                    OnlineEpisodeSelectorError = string.Format(_localizer.GetLoc(PlaybackErrorLocKey), ex.Message);
            });
        }
        finally
        {
            if (_onlineLaunchToken == ct)
                _onlineLaunchToken = null;
            FinishPlayerLaunch(ct);
        }
    }

    [RelayCommand]
    public async Task MarkEpisodeWatchedAsync(OnlineEpisodeItem? episodeItem)
    {
        if (_progressService == null || episodeItem == null || ActiveOnlineAnime is not { } onlineAnime ||
            !OnlineEpisodes.Contains(episodeItem) || ResolveMatchedAnime(onlineAnime.Title) is not { } matched)
            return;

        var key = new MediaKey(matched.Id, matched.MediaKind);
        var generation = _animeRepo.GetEntryGeneration(key.Id, key.Kind);
        try
        {
            await _progressService.MarkEpisodeWatchedAsync(key, generation, episodeItem.EpisodeNumber);
            if (ReferenceEquals(ActiveOnlineAnime, onlineAnime))
                UpdateEpisodeWatchedStates(_animeRepo.FindById(key.Id, key.Kind));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to mark episode {Episode} watched for {AnimeId}", episodeItem.EpisodeNumber, key.Id);
        }
    }

    private void UpdateEpisodeWatchedStates(AnimeEntity? anime)
    {
        if (OnlineEpisodes.Count == 0) return;

        var watchedProgress = anime?.Progress ?? 0;
        var isCompleted = anime?.Status == UserAnimeStatus.Completed;
        var hasNewEpisodes = anime is { Status: UserAnimeStatus.Watching } &&
            (anime.Presentation.HasNewEpisodes || anime.EpisodesAired > 0);

        foreach (var ep in OnlineEpisodes)
        {
            if (isCompleted || (watchedProgress > 0 && ep.EpisodeNumber <= watchedProgress))
            {
                ep.IsWatched = true;
                ep.IsNew = false;
            }
            else if ((!isCompleted && watchedProgress > 0 && ep.EpisodeNumber > watchedProgress) ||
                         (hasNewEpisodes && watchedProgress == 0))
            {
                ep.IsWatched = false;
                ep.IsNew = true;
            }
            else
            {
                ep.IsWatched = false;
                ep.IsNew = false;
            }
        }

        if (SelectedOnlineEpisode != null && SelectedOnlineEpisode.IsWatched && !_selectedEpisodeWasWatchedWhenSelected)
        {
            var nextUnwatched = OnlineEpisodes.FirstOrDefault(e => !e.IsWatched);
            if (nextUnwatched != null)
            {
                SelectOnlineEpisodeCommand.Execute(nextUnwatched);
            }
        }
    }

    public void OnAnimeProgressChanged(AnimeProgressChangedMessage message)
    {
        RefreshWatchingList();

        if (!IsOnlineEpisodeSelectorVisible || ActiveOnlineAnime == null)
            return;

        var targetAnime = _activeMatchedAnime ?? ResolveTrackedAnime(ActiveOnlineAnime.Title);
        if (targetAnime != null && (targetAnime.Id == message.Anime.Id || IsAnimeTitleMatch(message.Anime, ActiveOnlineAnime.Title)))
        {
            if (!ReferenceEquals(_activeMatchedAnime, targetAnime))
            {
                SetActiveMatchedAnime(targetAnime);
            }
            UpdateEpisodeWatchedStates(targetAnime);
        }
    }

    public void OnAnimeMatched(AnimeMatchedMessage message)
    {
        if (!IsOnlineEpisodeSelectorVisible || ActiveOnlineAnime == null || message.Anime is not { MediaKind: MediaKind.Anime })
            return;

        if (IsAnimeTitleMatch(message.Anime, ActiveOnlineAnime.Title) ||
            OnlineRelevanceScorer.ComputeRelevance(message.Anime.Title, ActiveOnlineAnime.Title) >= 0.65 ||
            (!string.IsNullOrWhiteSpace(message.Anime.RussianTitle) && OnlineRelevanceScorer.ComputeRelevance(message.Anime.RussianTitle, ActiveOnlineAnime.Title) >= 0.65))
        {
            var current = _animeRepo.FindById(message.Anime.Id, MediaKind.Anime);
            SetActiveMatchedAnime(current);
            UpdateEpisodeWatchedStates(current);
        }
    }

    public void OnAnimeListRefreshed()
    {
        RefreshWatchingList();
        RefreshOnlineEpisodeProgress();
    }

    private void RefreshOnlineEpisodeProgress()
    {
        if (IsOnlineEpisodeSelectorVisible && ActiveOnlineAnime != null)
        {
            var matched = ResolveTrackedAnime(ActiveOnlineAnime.Title);
            if (!ReferenceEquals(_activeMatchedAnime, matched))
                SetActiveMatchedAnime(matched);
            UpdateEpisodeWatchedStates(matched);
        }
    }

    private AnimeEntity? ResolveTrackedAnime(string title) => ResolveMatchedAnime(title) is { } matched
        ? _animeRepo.FindById(matched.Id, MediaKind.Anime)
        : null;

    public void SetActiveMatchedAnime(AnimeEntity? anime)
    {
        if (_activeMatchedAnime != null)
        {
            _activeMatchedAnime.PropertyChanged -= OnActiveMatchedAnimePropertyChanged;
        }

        _activeMatchedAnime = anime;

        if (_activeMatchedAnime != null)
        {
            _activeMatchedAnime.PropertyChanged += OnActiveMatchedAnimePropertyChanged;
        }
    }

    private void OnActiveMatchedAnimePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AnimeEntity.Progress) ||
            e.PropertyName == nameof(AnimeEntity.Status) ||
            e.PropertyName == nameof(AnimeEntity.Presentation))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (IsOnlineEpisodeSelectorVisible && _activeMatchedAnime != null)
                {
                    UpdateEpisodeWatchedStates(_activeMatchedAnime);
                }
            });
        }
    }

    private AnimeEntity? ResolveMatchedAnime(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var parsed = Kiriha.Core.Domain.Utils.Parsing.AnimeParseCache.Parse(title);
        int? parsedYear = parsed.Year;

        if (SelectedAnime is { MediaKind: MediaKind.Anime } &&
            IsOnlineYearCompatible(SelectedAnime, parsedYear, requireExplicitYear: false) &&
            IsAnimeTitleMatch(SelectedAnime, title))
        {
            return SelectedAnime;
        }

        var exact = ResolveAnimeForQuery(title);
        if (exact != null && IsOnlineYearCompatible(exact, parsedYear, requireExplicitYear: false))
            return exact;

        var norm = NormalizeTitle(title);
        if (!string.IsNullOrEmpty(norm))
        {
            var libraryExact = _animeRepo.Collection.FirstOrDefault(a =>
                a.MediaKind == MediaKind.Anime &&
                IsOnlineYearCompatible(a, parsedYear, requireExplicitYear: false) &&
                (NormalizeTitle(a.Title) == norm ||
                 (!string.IsNullOrEmpty(a.EnglishTitle) && NormalizeTitle(a.EnglishTitle) == norm) ||
                 (!string.IsNullOrEmpty(a.RussianTitle) && NormalizeTitle(a.RussianTitle) == norm) ||
                 a.AlternativeTitles.Any(alt => NormalizeTitle(alt) == norm)));

            if (libraryExact != null) return libraryExact;
        }

        if (SelectedAnime is { MediaKind: MediaKind.Anime } &&
            IsOnlineYearCompatible(SelectedAnime, parsedYear, requireExplicitYear: parsedYear.HasValue))
        {
            var targetTitle = SelectedAnime.RussianTitle ?? SelectedAnime.Title;
            if (!string.IsNullOrWhiteSpace(targetTitle) &&
                OnlineRelevanceScorer.ComputeRelevance(targetTitle, title) >= 0.75)
            {
                return SelectedAnime;
            }

            if (!string.IsNullOrWhiteSpace(SelectedAnime.Title) &&
                OnlineRelevanceScorer.ComputeRelevance(SelectedAnime.Title, title) >= 0.75)
            {
                return SelectedAnime;
            }
        }

        return _animeRepo.Collection.FirstOrDefault(a =>
            a.MediaKind == MediaKind.Anime &&
            IsOnlineYearCompatible(a, parsedYear, requireExplicitYear: parsedYear.HasValue) &&
            (OnlineRelevanceScorer.ComputeRelevance(a.Title, title) >= 0.75 ||
             (!string.IsNullOrEmpty(a.RussianTitle) && OnlineRelevanceScorer.ComputeRelevance(a.RussianTitle, title) >= 0.75)));
    }

    private static bool IsOnlineYearCompatible(AnimeEntity anime, int? parsedYear, bool requireExplicitYear)
    {
        if (!parsedYear.HasValue)
            return true;

        int? ay = anime.StartYear ?? anime.AiringDate?.Year;
        if (ay.HasValue)
            return Math.Abs(ay.Value - parsedYear.Value) <= 1;

        if (requireExplicitYear)
        {
            string ys = parsedYear.Value.ToString();
            return anime.Title.Contains(ys, StringComparison.Ordinal) ||
                   (anime.EnglishTitle?.Contains(ys, StringComparison.Ordinal) == true) ||
                   (anime.RussianTitle?.Contains(ys, StringComparison.Ordinal) == true);
        }

        return true;
    }

    private static bool IsAnimeTitleMatch(AnimeEntity anime, string title)
    {
        var norm = NormalizeTitle(title);
        if (string.IsNullOrEmpty(norm)) return false;

        return NormalizeTitle(anime.Title) == norm ||
               (!string.IsNullOrEmpty(anime.EnglishTitle) && NormalizeTitle(anime.EnglishTitle) == norm) ||
               (!string.IsNullOrEmpty(anime.RussianTitle) && NormalizeTitle(anime.RussianTitle) == norm) ||
               anime.AlternativeTitles.Any(alt => NormalizeTitle(alt) == norm);
    }

    private static string NormalizeTitle(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        return new string(input.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}
