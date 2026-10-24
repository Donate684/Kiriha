using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Streaming.Constants;
using Kiriha.Models;
using Kiriha.Utils;
using Kiriha.Utils.Async;
using Kiriha.ViewModels.Settings;
using Serilog;

namespace Kiriha.ViewModels.Torrents;

public partial class TorrentsViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveOnlineSources))]
    [NotifyPropertyChangedFor(nameof(ActiveOnlineSourcesCount))]
    [NotifyPropertyChangedFor(nameof(ActiveOnlineSourcesCountText))]
    [NotifyPropertyChangedFor(nameof(HasFilteredOnlineSources))]
    [NotifyPropertyChangedFor(nameof(AllOnlineSourcesActive))]
    private bool _filterOnlineAniLibria = true;

    partial void OnFilterOnlineAniLibriaChanged(bool value)
    {
        PersistOnlineFilters();
        TriggerOnlineSearchIfActive();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveOnlineSources))]
    [NotifyPropertyChangedFor(nameof(ActiveOnlineSourcesCount))]
    [NotifyPropertyChangedFor(nameof(ActiveOnlineSourcesCountText))]
    [NotifyPropertyChangedFor(nameof(HasFilteredOnlineSources))]
    [NotifyPropertyChangedFor(nameof(AllOnlineSourcesActive))]
    [NotifyPropertyChangedFor(nameof(ShowAnimeLibWarning))]
    private bool _filterOnlineAnimeLib = false;

    partial void OnFilterOnlineAnimeLibChanged(bool value)
    {
        PersistOnlineFilters();
        TriggerOnlineSearchIfActive();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAnimeLibWarning))]
    private bool _isAnimeLibLoggedIn;

    public bool ShowAnimeLibWarning => !IsAnimeLibLoggedIn && FilterOnlineAnimeLib;

    public void RefreshAnimeLibAuthStatus()
    {
        IsAnimeLibLoggedIn = !string.IsNullOrWhiteSpace(_settingsService.Current.Torrents.OnlineStreaming.AnimeLibToken);
    }

    [RelayCommand]
    public void NavigateToStreamingSettings()
    {
        WeakReferenceMessenger.Default.Send(new NavigationMessage(NavigationPage.Settings, SettingsViewModel.StreamingSectionIndex));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveOnlineSources))]
    [NotifyPropertyChangedFor(nameof(ActiveOnlineSourcesCount))]
    [NotifyPropertyChangedFor(nameof(ActiveOnlineSourcesCountText))]
    [NotifyPropertyChangedFor(nameof(HasFilteredOnlineSources))]
    [NotifyPropertyChangedFor(nameof(AllOnlineSourcesActive))]
    private bool _filterOnlineDreamCast = false;

    partial void OnFilterOnlineDreamCastChanged(bool value)
    {
        PersistOnlineFilters();
        TriggerOnlineSearchIfActive();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveOnlineSources))]
    [NotifyPropertyChangedFor(nameof(ActiveOnlineSourcesCount))]
    [NotifyPropertyChangedFor(nameof(ActiveOnlineSourcesCountText))]
    [NotifyPropertyChangedFor(nameof(HasFilteredOnlineSources))]
    [NotifyPropertyChangedFor(nameof(AllOnlineSourcesActive))]
    private bool _filterOnlineYummyAnime = false;

    partial void OnFilterOnlineYummyAnimeChanged(bool value)
    {
        PersistOnlineFilters();
        TriggerOnlineSearchIfActive();
    }

    private bool _filterOnlineAnime365 = false;

    public bool FilterOnlineAnime365
    {
        get => _filterOnlineAnime365;
        set
        {
            if (value && !IsAnime365LoggedIn)
            {
                ShowAnime365Warning = true;
                OnPropertyChanged(nameof(FilterOnlineAnime365));
                Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(FilterOnlineAnime365)));
                return;
            }

            if (SetProperty(ref _filterOnlineAnime365, value))
            {
                if (value)
                {
                    ShowAnime365Warning = false;
                }
                OnPropertyChanged(nameof(HasActiveOnlineSources));
                OnPropertyChanged(nameof(ActiveOnlineSourcesCount));
                OnPropertyChanged(nameof(ActiveOnlineSourcesCountText));
                OnPropertyChanged(nameof(HasFilteredOnlineSources));
                OnPropertyChanged(nameof(AllOnlineSourcesActive));
                PersistOnlineFilters();
                TriggerOnlineSearchIfActive();
            }
        }
    }

    [ObservableProperty]
    private bool _isAnime365LoggedIn;

    [ObservableProperty]
    private bool _showAnime365Warning;

    public void RefreshAnime365AuthStatus()
    {
        IsAnime365LoggedIn = !string.IsNullOrWhiteSpace(_settingsService.Current.Torrents.OnlineStreaming.Anime365Token);
        if (IsAnime365LoggedIn)
        {
            ShowAnime365Warning = false;
        }
        else
        {
            if (_filterOnlineAnime365)
            {
                _filterOnlineAnime365 = false;
                OnPropertyChanged(nameof(FilterOnlineAnime365));
                OnPropertyChanged(nameof(HasActiveOnlineSources));
                OnPropertyChanged(nameof(ActiveOnlineSourcesCount));
                OnPropertyChanged(nameof(ActiveOnlineSourcesCountText));
                OnPropertyChanged(nameof(HasFilteredOnlineSources));
                OnPropertyChanged(nameof(AllOnlineSourcesActive));
                PersistOnlineFilters();
            }
            ShowAnime365Warning = false;
        }
    }

    [RelayCommand]
    public void ToggleOnlineAnime365()
    {
        if (!IsAnime365LoggedIn)
        {
            ShowAnime365Warning = true;
            return;
        }

        FilterOnlineAnime365 = !FilterOnlineAnime365;
    }

    [ObservableProperty]
    private bool _hide480pSources;

    partial void OnHide480pSourcesChanged(bool value)
    {
        PersistOnlineFilters();
        NotifySourceCounts();
        ApplyOnlineSourceFilter();
    }

    [ObservableProperty]
    private bool _hide720pSources;

    partial void OnHide720pSourcesChanged(bool value)
    {
        PersistOnlineFilters();
        NotifySourceCounts();
        ApplyOnlineSourceFilter();
    }

    [ObservableProperty]
    private bool _hideNonRussianSources;

    partial void OnHideNonRussianSourcesChanged(bool value)
    {
        PersistOnlineFilters();
        NotifySourceCounts();
        ApplyOnlineSourceFilter();
    }

    public bool HasActiveOnlineSources => FilterOnlineAniLibria || FilterOnlineAnimeLib || FilterOnlineDreamCast || FilterOnlineYummyAnime || FilterOnlineAnime365;

    public int ActiveOnlineSourcesCount => (FilterOnlineAniLibria ? 1 : 0) + (FilterOnlineAnimeLib ? 1 : 0) + (FilterOnlineDreamCast ? 1 : 0) + (FilterOnlineYummyAnime ? 1 : 0) + (FilterOnlineAnime365 ? 1 : 0);

    public string ActiveOnlineSourcesCountText => $"{ActiveOnlineSourcesCount}/5";

    public bool HasFilteredOnlineSources => ActiveOnlineSourcesCount < 5;

    public bool AllOnlineSourcesActive => ActiveOnlineSourcesCount == 5;

    [RelayCommand]
    public void SelectAllOnlineSources()
    {
        FilterOnlineAniLibria = true;
        FilterOnlineAnimeLib = true;
        FilterOnlineDreamCast = true;
        FilterOnlineYummyAnime = true;
        if (IsAnime365LoggedIn)
        {
            FilterOnlineAnime365 = true;
        }
    }

    [RelayCommand]
    public void DeselectAllOnlineSources()
    {
        FilterOnlineAniLibria = false;
        FilterOnlineAnimeLib = false;
        FilterOnlineDreamCast = false;
        FilterOnlineYummyAnime = false;
        FilterOnlineAnime365 = false;
    }

    [RelayCommand]
    public void SelectOnlyOnlineSource(string? providerName)
    {
        if (string.IsNullOrEmpty(providerName)) return;
        FilterOnlineAniLibria = string.Equals(providerName, StreamingConstants.Providers.AniLibria, StringComparison.OrdinalIgnoreCase);
        FilterOnlineAnimeLib = string.Equals(providerName, StreamingConstants.Providers.AnimeLib, StringComparison.OrdinalIgnoreCase);
        FilterOnlineDreamCast = string.Equals(providerName, StreamingConstants.Providers.DreamCast, StringComparison.OrdinalIgnoreCase);
        FilterOnlineYummyAnime = string.Equals(providerName, StreamingConstants.Providers.YummyAnime, StringComparison.OrdinalIgnoreCase);
        FilterOnlineAnime365 = IsAnime365LoggedIn && string.Equals(providerName, StreamingConstants.Providers.Anime365, StringComparison.OrdinalIgnoreCase);
    }

    private bool _suppressOnlineFilterPersist;

    private void LoadOnlineFilterSettings()
    {
        var cfg = _settingsService.Current.Torrents.OnlineStreaming;
        RefreshAnimeLibAuthStatus();
        RefreshAnime365AuthStatus();
        _suppressOnlineFilterPersist = true;
        try
        {
            IsOnlineMode = _settingsService.Current.Torrents.IsOnlineMode;
            FilterOnlineAniLibria = cfg.EnableAniLibria;
            FilterOnlineAnimeLib = cfg.EnableAnimeLib;
            FilterOnlineDreamCast = cfg.EnableDreamCast;
            FilterOnlineYummyAnime = cfg.EnableYummyAnime;
            FilterOnlineAnime365 = IsAnime365LoggedIn && cfg.EnableAnime365;
            Hide480pSources = cfg.Hide480p;
            Hide720pSources = cfg.Hide720p;
            HideNonRussianSources = cfg.HideNonRussian;
        }
        finally
        {
            _suppressOnlineFilterPersist = false;
        }
    }

    private void PersistOnlineFilters()
    {
        if (_suppressOnlineFilterPersist) return;

        _settingsService.Update(settings =>
        {
            settings.Torrents.OnlineStreaming.EnableAniLibria = FilterOnlineAniLibria;
            settings.Torrents.OnlineStreaming.EnableAnimeLib = FilterOnlineAnimeLib;
            settings.Torrents.OnlineStreaming.EnableDreamCast = FilterOnlineDreamCast;
            settings.Torrents.OnlineStreaming.EnableYummyAnime = FilterOnlineYummyAnime;
            settings.Torrents.OnlineStreaming.EnableAnime365 = FilterOnlineAnime365;
            settings.Torrents.OnlineStreaming.Hide480p = Hide480pSources;
            settings.Torrents.OnlineStreaming.Hide720p = Hide720pSources;
            settings.Torrents.OnlineStreaming.HideNonRussian = HideNonRussianSources;
        }, SettingsSection.Torrents);
    }

    private void TriggerOnlineSearchIfActive()
    {
        if (_suppressOnlineFilterPersist) return;
        if (IsOnlineMode && (!string.IsNullOrWhiteSpace(SearchQuery) || SelectedAnime != null))
        {
            PerformSearchCommand.Execute(null);
        }
    }

    public List<string> GetEnabledOnlineProviders()
    {
        var list = new List<string>(5);
        if (FilterOnlineAniLibria) list.Add(StreamingConstants.Providers.AniLibria);
        if (FilterOnlineAnimeLib) list.Add(StreamingConstants.Providers.AnimeLib);
        if (FilterOnlineDreamCast) list.Add(StreamingConstants.Providers.DreamCast);
        if (FilterOnlineYummyAnime) list.Add(StreamingConstants.Providers.YummyAnime);
        if (FilterOnlineAnime365) list.Add(StreamingConstants.Providers.Anime365);
        return list;
    }

    partial void OnIsOnlineModeChanged(bool value)
    {
        if (!_suppressOnlineFilterPersist)
        {
            _settingsService.Update(settings => settings.Torrents.IsOnlineMode = value, SettingsSection.Torrents);
        }

        if (SelectedAnime != null)
        {
            SearchQuery = value && !string.IsNullOrWhiteSpace(SelectedAnime.RussianTitle)
                ? SelectedAnime.RussianTitle
                : SelectedAnime.Title;
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery) || SelectedAnime != null)
        {
            PerformSearchCommand.Execute(null);
        }
    }

    [RelayCommand]
    public void ToggleSearchMode()
    {
        IsOnlineMode = !IsOnlineMode;
    }

    private async Task PerformOnlineSearchAsync(string queryText)
    {
        var query = queryText.Trim();
        if (string.IsNullOrEmpty(query))
        {
            if (SelectedAnime != null)
            {
                query = !string.IsNullOrWhiteSpace(SelectedAnime.RussianTitle)
                    ? SelectedAnime.RussianTitle
                    : SelectedAnime.Title;
            }
        }

        if (string.IsNullOrEmpty(query))
        {
            OnlineResults.Clear();
            return;
        }

        var enabledProviders = GetEnabledOnlineProviders();
        if (enabledProviders.Count == 0)
        {
            OnlineResults.Clear();
            return;
        }

        if (_streamingService == null)
        {
            Log.Warning("[Torrents] StreamingService is not registered");
            return;
        }

        _streamingCts?.Cancel();
        _streamingCts?.Dispose();
        _streamingCts = new CancellationTokenSource();
        var ct = _streamingCts.Token;

        try
        {
            IsLoading = true;
            Log.Information("[Torrents.Online] Searching for '{Query}' with providers: {Providers}", query, string.Join(", ", enabledProviders));

            var searchTargets = new List<string> { query };
            if (SelectedAnime != null)
            {
                if (!string.IsNullOrWhiteSpace(SelectedAnime.RussianTitle))
                    searchTargets.Add(SelectedAnime.RussianTitle);
                if (!string.IsNullOrWhiteSpace(SelectedAnime.Title))
                    searchTargets.Add(SelectedAnime.Title);
                if (!string.IsNullOrWhiteSpace(SelectedAnime.EnglishTitle))
                    searchTargets.Add(SelectedAnime.EnglishTitle);
            }

            var allItems = new List<OnlineAnimeItem>();
            var hasAny = false;

            await foreach (var batch in _streamingService.SearchStreamAsync(query, enabledProviders, ct).ConfigureAwait(false))
            {
                if (batch.Count == 0) continue;
                var newItems = batch.Select(r => new OnlineAnimeItem(r, OnlineRelevanceScorer.ComputeMaxRelevance(searchTargets, r.Title))).ToList();
                allItems.AddRange(newItems);

                var snapshot = allItems
                    .OrderByDescending(x => x.RelevanceScore)
                    .ThenBy(x => x.ProviderName)
                    .ToList();

                Dispatcher.UIThread.Post(() =>
                {
                    OnlineResults.Reset(snapshot);
                    if (!hasAny)
                    {
                        hasAny = true;
                        IsLoading = false;
                    }
                });
            }

            Log.Information("[Torrents.Online] Search completed with {Count} items", allItems.Count);
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Torrents.Online] Search failed for '{Query}'", query);
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsLoading = false);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterAllSelected))]
    [NotifyPropertyChangedFor(nameof(IsFilterDirectSelected))]
    [NotifyPropertyChangedFor(nameof(IsFilterDubSelected))]
    [NotifyPropertyChangedFor(nameof(IsFilterSubSelected))]
    private string _onlineSourceFilter = "dub";

    partial void OnOnlineSourceFilterChanged(string value)
    {
        ApplyOnlineSourceFilter();
    }

    public bool IsFilterAllSelected => OnlineSourceFilter == "all";
    public bool IsFilterDirectSelected => OnlineSourceFilter == "direct";
    public bool IsFilterDubSelected => OnlineSourceFilter == "dub";
    public bool IsFilterSubSelected => OnlineSourceFilter == "sub";

    public int DirectSourcesCount => OnlineSources.Count(s => s.IsDirectMp4 && (!Hide480pSources || !s.Is480pOrLower) && (!Hide720pSources || !s.Is720p) && (!HideNonRussianSources || !s.IsNonRussian));
    public int DubSourcesCount => OnlineSources.Count(s => !s.IsSubtitles && (!Hide480pSources || !s.Is480pOrLower) && (!Hide720pSources || !s.Is720p) && (!HideNonRussianSources || !s.IsNonRussian));
    public int SubSourcesCount => OnlineSources.Count(s => s.IsSubtitles && (!Hide480pSources || !s.Is480pOrLower) && (!Hide720pSources || !s.Is720p) && (!HideNonRussianSources || !s.IsNonRussian));
    public int AllSourcesCount => OnlineSources.Count(s => (!Hide480pSources || !s.Is480pOrLower) && (!Hide720pSources || !s.Is720p) && (!HideNonRussianSources || !s.IsNonRussian));

    public bool HasDirectSources => DirectSourcesCount > 0;
    public bool HasDubSources => DubSourcesCount > 0;
    public bool HasSubSources => SubSourcesCount > 0;
    public bool HasNoDisplayedSources => DisplayedOnlineSources.Count == 0 && !IsOnlineSourcesLoading && OnlineSources.Count > 0;

    [RelayCommand]
    public void SetOnlineSourceFilter(string? filter)
    {
        var target = string.Equals(filter, "sub", StringComparison.OrdinalIgnoreCase) ? "sub" : "dub";
        OnlineSourceFilter = target;
        _settingsService.Update(s => s.Torrents.OnlineStreaming.PreferredSourceType = target, SettingsSection.Torrents);
    }

    private void ApplyOnlineSourceFilter()
    {
        var byType = OnlineSourceFilter switch
        {
            "sub" => OnlineSources.Where(s => s.IsSubtitles),
            _ => OnlineSources.Where(s => !s.IsSubtitles)
        };

        var typeList = byType.ToList();
        if (typeList.Count == 0 && OnlineSources.Count > 0)
        {
            typeList = OnlineSources.ToList();
        }

        IEnumerable<OnlineSourceItem> filtered = typeList;
        if (Hide480pSources)
        {
            filtered = filtered.Where(s => !s.Is480pOrLower);
        }

        if (Hide720pSources)
        {
            filtered = filtered.Where(s => !s.Is720p);
        }

        if (HideNonRussianSources)
        {
            filtered = filtered.Where(s => !s.IsNonRussian);
        }

        var list = filtered.ToList();

        DisplayedOnlineSources.Clear();
        foreach (var item in list)
        {
            DisplayedOnlineSources.Add(item);
        }

        OnPropertyChanged(nameof(HasNoDisplayedSources));
    }

    private void NotifySourceCounts()
    {
        OnPropertyChanged(nameof(AllSourcesCount));
        OnPropertyChanged(nameof(DirectSourcesCount));
        OnPropertyChanged(nameof(DubSourcesCount));
        OnPropertyChanged(nameof(SubSourcesCount));
        OnPropertyChanged(nameof(HasDirectSources));
        OnPropertyChanged(nameof(HasDubSources));
        OnPropertyChanged(nameof(HasSubSources));
    }

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

        _streamingCts?.Cancel();
        _streamingCts?.Dispose();
        _streamingCts = new CancellationTokenSource();
        var ct = _streamingCts.Token;

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
                    OnlineEpisodeSelectorError = _localizer.GetLoc("torrents.online.no_episodes");
                    IsOnlineEpisodeSelectorLoading = false;
                });
                return;
            }

            var matchedAnime = ResolveMatchedAnime(item.Title);
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
                OnlineEpisodes.Clear();
                foreach (var ep in episodes)
                {
                    OnlineEpisodes.Add(ep);
                }
                OnlineEpisodeSelectorCountText = _localizer.GetLoc(EpisodesCountLocKey, OnlineEpisodes.Count);
                IsOnlineEpisodeSelectorLoading = false;

                if (OnlineEpisodes.Count > 0)
                {
                    SelectOnlineEpisodeCommand.Execute(OnlineEpisodes[0]);
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
                OnlineEpisodeSelectorError = string.Format(_localizer.GetLoc(PlaybackErrorLocKey), ex.Message);
                IsOnlineEpisodeSelectorLoading = false;
            });
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
        OnlineSources.Clear();
        DisplayedOnlineSources.Clear();
        IsOnlineSourcesLoading = true;
        OnlineEpisodeSelectorError = null;

        _streamingCts?.Cancel();
        _streamingCts?.Dispose();
        _streamingCts = new CancellationTokenSource();
        var ct = _streamingCts.Token;

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
                OnlineEpisodeSelectorError = string.Format(_localizer.GetLoc(PlaybackErrorLocKey), ex.Message);
                IsOnlineSourcesLoading = false;
            });
        }
    }

    [RelayCommand]
    public async Task PlayOnlineSourceAsync(OnlineSourceItem? sourceItem)
    {
        if (sourceItem == null || _streamingService == null || ActiveOnlineAnime == null) return;

        var animeTitle = ActiveOnlineAnime.Title;
        var epTitle = SelectedOnlineEpisode?.Title ?? string.Empty;
        var epNum = SelectedOnlineEpisode?.EpisodeNumber.ToString();

        IsPlayerLaunching = true;
        PlayerLaunchingTitle = $"{animeTitle} - {epTitle} ({sourceItem.Title})";

        _streamingCts?.Cancel();
        _streamingCts?.Dispose();
        _streamingCts = new CancellationTokenSource();
        var ct = _streamingCts.Token;

        try
        {
            var videos = await _streamingService.ResolveStreamAsync(sourceItem.PlayerUrl, ct).ConfigureAwait(false);
            var targetVideo = videos.OrderByDescending(v => v.Quality).FirstOrDefault();
            var streamUrl = targetVideo?.Url ?? sourceItem.PlayerUrl;
            var subtitleUrl = targetVideo?.SubtitleUrl ?? sourceItem.SubtitleUrl;

            if (string.IsNullOrWhiteSpace(streamUrl) || streamUrl.Contains(StreamingConstants.Anime365.EmbedPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(_localizer.GetLoc("torrents.online.stream_not_found"));
            }

            var matchedAnime = ResolveMatchedAnime(animeTitle);
            var titleRu = matchedAnime?.RussianTitle ?? animeTitle;
            var titleEn = matchedAnime?.Title ?? matchedAnime?.EnglishTitle ?? animeTitle;
            var originalTitle = !string.IsNullOrWhiteSpace(epNum)
                ? $"{titleEn} - {epNum}"
                : titleEn;

            if (_internalPlayerServer != null)
            {
                var initialMeta = new PlayerMediaMetadata(
                    originalTitle,
                    titleRu,
                    titleEn,
                    epNum ?? string.Empty,
                    matchedAnime?.Id,
                    titleEn,
                    TorrentHash: string.Empty);

                _internalPlayerServer.SendMetadataAsync(initialMeta).SafeFireAndForget("Torrents.Online.SendMetadata");
            }

            PlayerProcessHelper.LaunchPlayer(
                videoUrl: streamUrl,
                titleRu: titleRu,
                titleEn: titleEn,
                episode: epNum,
                animeId: matchedAnime?.Id,
                originalTitle: originalTitle,
                subtitleUrl: subtitleUrl);
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Torrents.Online] Failed to resolve video for '{Url}'", sourceItem.PlayerUrl);
            Dispatcher.UIThread.Post(() =>
            {
                OnlineEpisodeSelectorError = string.Format(_localizer.GetLoc(PlaybackErrorLocKey), ex.Message);
            });
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsPlayerLaunching = false);
        }
    }

    [RelayCommand]
    public async Task MarkEpisodeWatchedAsync(OnlineEpisodeItem? episodeItem)
    {
        if (episodeItem == null || ActiveOnlineAnime == null) return;
        var matchedAnime = ResolveMatchedAnime(ActiveOnlineAnime.Title);
        if (matchedAnime == null) return;

        var newProgress = episodeItem.EpisodeNumber;
        matchedAnime.Progress = newProgress;
        if (matchedAnime.TotalEpisodes > 0 && newProgress >= matchedAnime.TotalEpisodes)
        {
            matchedAnime.Status = UserAnimeStatus.Completed;
        }
        else if (matchedAnime.Status != UserAnimeStatus.Watching)
        {
            matchedAnime.Status = UserAnimeStatus.Watching;
        }

        await _animeRepo.AddOrUpdateAnimeAsync(matchedAnime).ConfigureAwait(false);
        Dispatcher.UIThread.Post(() =>
        {
            UpdateEpisodeWatchedStates(matchedAnime);
        });
    }

    private void UpdateEpisodeWatchedStates(AnimeEntity? anime)
    {
        if (anime == null || OnlineEpisodes.Count == 0) return;

        var watchedProgress = anime.Progress;
        var isCompleted = anime.Status == UserAnimeStatus.Completed;
        var isWatching = anime.Status == UserAnimeStatus.Watching;

        foreach (var ep in OnlineEpisodes)
        {
            if (isCompleted)
            {
                ep.IsWatched = true;
                ep.IsNew = false;
            }
            else if (watchedProgress > 0 && ep.EpisodeNumber <= watchedProgress)
            {
                ep.IsWatched = true;
                ep.IsNew = false;
            }
            else if (!isCompleted && watchedProgress > 0 && ep.EpisodeNumber > watchedProgress)
            {
                ep.IsWatched = false;
                ep.IsNew = true;
            }
            else if (isWatching && watchedProgress == 0 && (anime.Presentation.HasNewEpisodes || anime.EpisodesAired > 0))
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
    }

    private AnimeEntity? ResolveMatchedAnime(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        if (SelectedAnime != null)
        {
            if (IsAnimeTitleMatch(SelectedAnime, title))
                return SelectedAnime;

            var targetTitle = SelectedAnime.RussianTitle ?? SelectedAnime.Title;
            if (!string.IsNullOrWhiteSpace(targetTitle) &&
                OnlineRelevanceScorer.ComputeRelevance(targetTitle, title) >= 0.65)
            {
                return SelectedAnime;
            }

            if (!string.IsNullOrWhiteSpace(SelectedAnime.Title) &&
                OnlineRelevanceScorer.ComputeRelevance(SelectedAnime.Title, title) >= 0.65)
            {
                return SelectedAnime;
            }
        }

        var exact = ResolveAnimeForQuery(title);
        if (exact != null) return exact;

        var norm = NormalizeTitle(title);
        if (string.IsNullOrEmpty(norm)) return null;

        return _animeRepo.Collection.FirstOrDefault(a =>
            NormalizeTitle(a.Title) == norm ||
            (!string.IsNullOrEmpty(a.EnglishTitle) && NormalizeTitle(a.EnglishTitle) == norm) ||
            (!string.IsNullOrEmpty(a.RussianTitle) && NormalizeTitle(a.RussianTitle) == norm));
    }

    private static bool IsAnimeTitleMatch(AnimeEntity anime, string title)
    {
        var norm = NormalizeTitle(title);
        if (string.IsNullOrEmpty(norm)) return false;

        return NormalizeTitle(anime.Title) == norm ||
               (!string.IsNullOrEmpty(anime.EnglishTitle) && NormalizeTitle(anime.EnglishTitle) == norm) ||
               (!string.IsNullOrEmpty(anime.RussianTitle) && NormalizeTitle(anime.RussianTitle) == norm);
    }

    private static string NormalizeTitle(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        return new string(input.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    [RelayCommand]
    public void CloseOnlineEpisodeSelector()
    {
        _streamingCts?.Cancel();
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
