using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Streaming.Constants;
using Kiriha.Models;
using Serilog;

namespace Kiriha.ViewModels.Torrents;

public partial class TorrentsViewModel
{
    [RelayCommand]
    public async Task OpenOnlineAnimeAsync(OnlineAnimeItem? item)
    {
        if (item == null || _streamingService == null) return;

        ActiveOnlineAnime = item;
        OnlineEpisodes.Clear();
        OnlineSources.Clear();
        DisplayedOnlineSources.Clear();
        OnlineSourceFilter = _settingsService.Current.Torrents.OnlineStreaming.PreferredSourceType ?? "dub";
        NotifySourceCounts();
        SelectedOnlineEpisode = null;
        IsOnlineEpisodeSelectorVisible = true;
        IsOnlineEpisodeSelectorLoading = true;
        OnlineEpisodeSelectorError = null;
        OnlineEpisodeSelectorCountText = string.Empty;

        _modalCts?.Cancel();
        _modalCts?.Dispose();
        _modalCts = new CancellationTokenSource();
        var ct = _modalCts.Token;

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

            var matchedAnime = ResolveMatchedAnime(item.Title);
            Dispatcher.UIThread.Post(() =>
            {
                if (ct.IsCancellationRequested) return;
                SetActiveMatchedAnime(matchedAnime);
            });
            var watchedProgress = matchedAnime?.Progress ?? 0;
            var isCompleted = matchedAnime?.Status == UserAnimeStatus.Completed;
            var isWatching = matchedAnime?.Status == UserAnimeStatus.Watching;

            var episodes = details.Episodes.Select(e =>
            {
                var isWatched = isCompleted || (watchedProgress > 0 && e.EpisodeNumber <= watchedProgress);
                var isNew = !isWatched && !isCompleted && ((watchedProgress > 0 && e.EpisodeNumber > watchedProgress) || (isWatching && watchedProgress == 0 && (matchedAnime?.Presentation.HasNewEpisodes == true || matchedAnime?.EpisodesAired > 0)));
                return new OnlineEpisodeItem(e, _localizer, isWatched, isNew);
            }).ToList();

            Dispatcher.UIThread.Post(() =>
            {
                if (ct.IsCancellationRequested) return;
                OnlineEpisodes.Clear();
                foreach (var ep in episodes)
                {
                    OnlineEpisodes.Add(ep);
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
        catch (OperationCanceledException)
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
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                Dispatcher.UIThread.Post(() => IsOnlineEpisodeSelectorLoading = false);
            }
        }
    }

    [RelayCommand]
    public async Task SelectOnlineEpisodeAsync(OnlineEpisodeItem? episodeItem)
    {
        if (episodeItem == null || ActiveOnlineAnime == null || _streamingService == null) return;

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

        _modalCts?.Cancel();
        _modalCts?.Dispose();
        _modalCts = new CancellationTokenSource();
        var ct = _modalCts.Token;

        try
        {
            var targetId = !string.IsNullOrWhiteSpace(episodeItem.Episode.EpisodeId) ? episodeItem.Episode.EpisodeId : ActiveOnlineAnime.Id;
            var sources = episodeItem.Episode.Sources.Count > 0
                ? episodeItem.Episode.Sources
                : await _streamingService.GetEpisodeSourcesAsync(ActiveOnlineAnime.ProviderName, targetId, episodeItem.EpisodeNumber, ct).ConfigureAwait(false);

            var sourceItems = sources.Select(s => new OnlineSourceItem(s)).ToList();

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
        catch (OperationCanceledException)
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
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                Dispatcher.UIThread.Post(() => IsOnlineSourcesLoading = false);
            }
        }
    }

    internal static bool IsDirectMediaUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var path = uri.AbsolutePath;
        return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    public void CloseOnlineEpisodeSelector()
    {
        _modalCts?.Cancel();
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
        OnlineEpisodeSelectorCountText = string.Empty;
        NotifySourceCounts();
    }
}
