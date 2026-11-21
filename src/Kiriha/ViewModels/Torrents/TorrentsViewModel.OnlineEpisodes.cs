using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Streaming.Constants;
using Kiriha.Core.Streaming.Services;
using Kiriha.Models;
using Serilog;

namespace Kiriha.ViewModels.Torrents;

public partial class TorrentsViewModel
{
    private CancellationTokenSource? _onlineSelection;
    private string? _onlineDetailsId;

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task OpenOnlineAnimeAsync(OnlineAnimeItem? item)
    {
        if (item == null || _streamingService == null) return;
        var ct = BeginOnlineSelection();

        ActiveOnlineAnime = item;
        _onlineDetailsId = null;
        SetActiveMatchedAnime(null);
        OnlineEpisodes.Clear();
        OnlineSources.Clear();
        DisplayedOnlineSources.Clear();
        OnlineSourceFilter = _settingsService.Current.Torrents.OnlineStreaming.PreferredSourceType ?? "dub";
        NotifySourceCounts();
        SelectedOnlineEpisode = null;
        IsOnlineEpisodeSelectorVisible = true;
        IsOnlineEpisodeSelectorLoading = true;
        IsOnlineSourcesLoading = false;
        OnlineEpisodeSelectorError = null;
        OnlineEpisodeSelectorCountText = string.Empty;

        try
        {
            var targetIdOrUrl = string.Equals(item.ProviderName, StreamingConstants.Providers.DreamCast, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.Url)
                ? item.Url
                : (!string.IsNullOrWhiteSpace(item.Id) ? item.Id : item.Url);
            var details = await _streamingService.GetAnimeDetailsAsync(item.ProviderName, targetIdOrUrl, ct).ConfigureAwait(false);

            if (details == null || details.Episodes.Count == 0)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    OnlineEpisodeSelectorError = _localizer.GetLoc("torrents.online.no_episodes");
                    IsOnlineEpisodeSelectorLoading = false;
                });
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (ct.IsCancellationRequested) return;
                _onlineDetailsId = details.Id;
                var matchedAnime = ResolveMatchedAnime(item.Title);
                SetActiveMatchedAnime(matchedAnime);
                var watchedProgress = matchedAnime?.Progress ?? 0;
                var isCompleted = matchedAnime?.Status == UserAnimeStatus.Completed;
                var isWatching = matchedAnime?.Status == UserAnimeStatus.Watching;
                OnlineEpisodes.Clear();
                foreach (var episode in details.Episodes)
                {
                    var isWatched = isCompleted || (watchedProgress > 0 && episode.EpisodeNumber <= watchedProgress);
                    var isNew = !isWatched && (watchedProgress > 0 ||
                        (isWatching && (matchedAnime?.Presentation.HasNewEpisodes == true || matchedAnime?.EpisodesAired > 0)));
                    OnlineEpisodes.Add(new OnlineEpisodeItem(episode, _localizer, isWatched, isNew));
                }
                OnlineEpisodeSelectorCountText = _localizer.GetLoc(EpisodesCountLocKey, OnlineEpisodes.Count);
                IsOnlineEpisodeSelectorLoading = false;

                if (OnlineEpisodes.Count > 0)
                {
                    var targetEp = OnlineEpisodes.FirstOrDefault(e => !e.IsWatched) ?? OnlineEpisodes[0];
                    SelectOnlineEpisodeCommand.Execute(targetEp);
                }
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelled
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Torrents.Online] Failed to load anime details for '{Title}'", item.Title);
            Dispatcher.UIThread.Post(() =>
            {
                if (ct.IsCancellationRequested) return;
                OnlineEpisodeSelectorError = string.Format(_localizer.GetLoc(PlaybackErrorLocKey), ex.Message);
                IsOnlineEpisodeSelectorLoading = false;
            });
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task SelectOnlineEpisodeAsync(OnlineEpisodeItem? episodeItem)
    {
        if (episodeItem == null || ActiveOnlineAnime is not { } anime || _streamingService == null ||
            !OnlineEpisodes.Contains(episodeItem)) return;
        var ct = BeginOnlineSelection();

        foreach (var ep in OnlineEpisodes)
        {
            ep.IsSelected = ep == episodeItem;
        }

        SelectedOnlineEpisode = episodeItem;
        _selectedEpisodeWasWatchedWhenSelected = episodeItem.IsWatched;
        OnlineSources.Clear();
        DisplayedOnlineSources.Clear();
        IsOnlineSourcesLoading = true;
        OnlineEpisodeSelectorError = null;
        NotifySourceCounts();

        try
        {
            var episodeId = episodeItem.Episode.EpisodeId;
            var targetId = (anime.IsAniLibria, anime.IsAnime365, episodeId) switch
            {
                (true, _, _) => _onlineDetailsId ?? anime.Id,
                (_, _, { Length: > 0 } id) when !string.IsNullOrWhiteSpace(id) => id,
                (_, true, _) when !string.IsNullOrWhiteSpace(anime.Url) => anime.Url,
                _ => anime.Id
            };
            var sources = episodeItem.Episode.Sources.Count > 0
                ? episodeItem.Episode.Sources
                : await _streamingService.GetEpisodeSourcesAsync(anime.ProviderName, targetId, episodeItem.EpisodeNumber, ct).ConfigureAwait(false);

            var sourceItems = sources.Where(s => StreamingMediaUrl.IsHttp(s.PlayerUrl)).Select(s => new OnlineSourceItem(s));

            // Sort by Direct MP4 first, then 1080p FHD, then 720p HD, then others
            var sorted = sourceItems
                .OrderByDescending(s => s.IsDirectMp4)
                .ThenByDescending(s => s.IsFhd)
                .ThenByDescending(s => s.IsHd)
                .ToList();

            Dispatcher.UIThread.Post(() =>
            {
                if (ct.IsCancellationRequested) return;
                OnlineSources.Clear();
                foreach (var s in sorted)
                {
                    OnlineSources.Add(s);
                }

                NotifySourceCounts();

                // Apply preferred filter (dub or sub) from settings, fallback if 0 sources for preferred
                var preferred = _settingsService.Current.Torrents.OnlineStreaming.PreferredSourceType;
                if (string.Equals(preferred, "sub", StringComparison.OrdinalIgnoreCase))
                {
                    OnlineSourceFilter = HasSubSources ? "sub" : (HasDubSources ? "dub" : "sub");
                }
                else
                {
                    OnlineSourceFilter = HasDubSources ? "dub" : (HasSubSources ? "sub" : "dub");
                }

                ApplyOnlineSourceFilter();

                if (OnlineSources.Count == 0)
                {
                    OnlineEpisodeSelectorError = _localizer.GetLoc("torrents.online.no_sources");
                }
                IsOnlineSourcesLoading = false;
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelled
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Torrents.Online] Failed to load sources for ep {Ep}", episodeItem.EpisodeNumber);
            Dispatcher.UIThread.Post(() =>
            {
                if (ct.IsCancellationRequested) return;
                OnlineEpisodeSelectorError = string.Format(_localizer.GetLoc(PlaybackErrorLocKey), ex.Message);
                IsOnlineSourcesLoading = false;
            });
        }
    }

    internal static bool IsDirectMediaUrl(string? url) => StreamingMediaUrl.IsDirect(url);

    private CancellationToken BeginOnlineSelection()
    {
        CancelOnlineSelection();
        _onlineSelection = new CancellationTokenSource();
        return _onlineSelection.Token;
    }

    private void CancelOnlineSelection()
    {
        _onlineSelection?.Cancel();
        _onlineSelection?.Dispose();
        _onlineSelection = null;
        if (_onlineLaunchToken is { } active && _launchCts?.Token == active)
        {
            _launchCts.Cancel();
            IsPlayerLaunching = false;
        }
        _onlineLaunchToken = null;
    }

    [RelayCommand]
    public void CloseOnlineEpisodeSelector()
    {
        CancelOnlineSelection();
        SetActiveMatchedAnime(null);
        _selectedEpisodeWasWatchedWhenSelected = false;
        IsOnlineEpisodeSelectorVisible = false;
        IsOnlineEpisodeSelectorLoading = false;
        IsOnlineSourcesLoading = false;
        OnlineEpisodeSelectorError = null;
        OnlineEpisodes.Clear();
        OnlineSources.Clear();
        DisplayedOnlineSources.Clear();
        OnlineSourceFilter = _settingsService.Current.Torrents.OnlineStreaming.PreferredSourceType ?? "dub";
        SelectedOnlineEpisode = null;
        ActiveOnlineAnime = null;
        _onlineDetailsId = null;
        OnlineEpisodeSelectorCountText = string.Empty;
        NotifySourceCounts();
    }
}
