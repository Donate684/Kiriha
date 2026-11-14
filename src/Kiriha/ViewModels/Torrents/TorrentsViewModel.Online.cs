using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Messages;
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
    [NotifyPropertyChangedFor(nameof(ShowActiveOnlineFhdBadge))]
    private bool _isAnimeLibLoggedIn;

    public bool ShowAnimeLibWarning => !IsAnimeLibLoggedIn && FilterOnlineAnimeLib;

    public bool ShowActiveOnlineFhdBadge => IsAnimeLibLoggedIn && ActiveOnlineAnime?.IsAnimeLib == true;

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
        _suppressOnlineFilterPersist = true;
        try
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
        finally
        {
            _suppressOnlineFilterPersist = false;
        }
        PersistOnlineFilters();
        TriggerOnlineSearchIfActive();
    }

    [RelayCommand]
    public void DeselectAllOnlineSources()
    {
        _suppressOnlineFilterPersist = true;
        try
        {
            FilterOnlineAniLibria = false;
            FilterOnlineAnimeLib = false;
            FilterOnlineDreamCast = false;
            FilterOnlineYummyAnime = false;
            FilterOnlineAnime365 = false;
        }
        finally
        {
            _suppressOnlineFilterPersist = false;
        }
        PersistOnlineFilters();
        TriggerOnlineSearchIfActive();
    }

    [RelayCommand]
    public void SelectOnlyOnlineSource(string? providerName)
    {
        if (string.IsNullOrEmpty(providerName)) return;
        _suppressOnlineFilterPersist = true;
        try
        {
            FilterOnlineAniLibria = string.Equals(providerName, StreamingConstants.Providers.AniLibria, StringComparison.OrdinalIgnoreCase);
            FilterOnlineAnimeLib = string.Equals(providerName, StreamingConstants.Providers.AnimeLib, StringComparison.OrdinalIgnoreCase);
            FilterOnlineDreamCast = string.Equals(providerName, StreamingConstants.Providers.DreamCast, StringComparison.OrdinalIgnoreCase);
            FilterOnlineYummyAnime = string.Equals(providerName, StreamingConstants.Providers.YummyAnime, StringComparison.OrdinalIgnoreCase);
            FilterOnlineAnime365 = IsAnime365LoggedIn && string.Equals(providerName, StreamingConstants.Providers.Anime365, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _suppressOnlineFilterPersist = false;
        }
        PersistOnlineFilters();
        TriggerOnlineSearchIfActive();
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
            var nextQuery = value && !string.IsNullOrWhiteSpace(SelectedAnime.RussianTitle)
                ? SelectedAnime.RussianTitle
                : (!value && UseCustomQuery && !string.IsNullOrWhiteSpace(CustomQuery)
                    ? CustomQuery
                    : SelectedAnime.Title);
            SetSearchQueryFromContext(nextQuery);
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

    private async Task PerformOnlineSearchAsync(string queryText, CancellationToken ct = default)
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

        if (ct == default)
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            ct = _searchCts.Token;
        }

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
                if (ct.IsCancellationRequested) return;
                if (batch.Count == 0) continue;
                var newItems = batch.Select(r => new OnlineAnimeItem(r, OnlineRelevanceScorer.ComputeMaxRelevance(searchTargets, r.Title))).ToList();
                allItems.AddRange(newItems);

                var snapshot = allItems
                    .OrderByDescending(x => x.RelevanceScore)
                    .ThenBy(x => x.ProviderName)
                    .ToList();

                Dispatcher.UIThread.Post(() =>
                {
                    if (ct.IsCancellationRequested) return;
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
            if (!ct.IsCancellationRequested)
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
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
        IEnumerable<OnlineSourceItem> filtered = OnlineSourceFilter switch
        {
            "sub" => OnlineSources.Where(s => s.IsSubtitles),
            _ => OnlineSources.Where(s => !s.IsSubtitles)
        };

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
    public async Task PlayOnlineSourceAsync(OnlineSourceItem? sourceItem)
    {
        if (sourceItem == null || _streamingService == null || ActiveOnlineAnime == null) return;

        var animeTitle = ActiveOnlineAnime.Title;
        var epTitle = SelectedOnlineEpisode?.Title ?? string.Empty;
        var epNum = SelectedOnlineEpisode?.EpisodeNumber.ToString();

        IsPlayerLaunching = true;
        PlayerLaunchingTitle = $"{animeTitle} - {epTitle} ({sourceItem.Title})";
        OnlineEpisodeSelectorError = null;

        _launchCts?.Cancel();
        _launchCts?.Dispose();
        _launchCts = new CancellationTokenSource();
        var ct = _launchCts.Token;

        try
        {
            var videos = await _streamingService.ResolveStreamAsync(sourceItem.PlayerUrl, ct).ConfigureAwait(false);
            var validVideos = videos.Where(v => !string.Equals(v.VideoType, "embed", StringComparison.OrdinalIgnoreCase)).ToList();

            var preferDirect = _settingsService?.Current.Torrents.OnlineStreaming.PreferDirectMp4 ?? false;
            var targetVideo = validVideos
                .OrderByDescending(v => preferDirect && (string.Equals(v.VideoType, "mp4", StringComparison.OrdinalIgnoreCase) || v.Url.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)))
                .ThenByDescending(v => v.Quality)
                .FirstOrDefault();

            string? streamUrl = null;
            if (targetVideo != null && (IsDirectMediaUrl(targetVideo.Url) || targetVideo.VideoType is "m3u8" or "mp4" or "mpd"))
            {
                streamUrl = targetVideo.Url;
            }
            else if (IsDirectMediaUrl(sourceItem.PlayerUrl))
            {
                streamUrl = sourceItem.PlayerUrl;
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
                subtitleUrl: subtitleUrl,
                headers: headers);
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
            if (!ct.IsCancellationRequested)
            {
                Dispatcher.UIThread.Post(() => IsPlayerLaunching = false);
            }
        }
    }

    [RelayCommand]
    public async Task MarkEpisodeWatchedAsync(OnlineEpisodeItem? episodeItem)
    {
        if (episodeItem == null || ActiveOnlineAnime == null) return;
        var matchedAnime = ResolveMatchedAnime(ActiveOnlineAnime.Title);
        if (matchedAnime == null) return;

        var newProgress = episodeItem.EpisodeNumber;
        if (_progressService != null)
        {
            await _progressService.SmartIncrementProgressAsync(matchedAnime, newProgress).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() => UpdateEpisodeWatchedStates(matchedAnime));
        }
        else
        {
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
                WeakReferenceMessenger.Default.Send(new AnimeProgressChangedMessage(matchedAnime, newProgress));
            });
        }
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

        if (SelectedOnlineEpisode != null && SelectedOnlineEpisode.IsWatched && !_selectedEpisodeWasWatchedWhenSelected)
        {
            var nextUnwatched = OnlineEpisodes.FirstOrDefault(e => !e.IsWatched);
            if (nextUnwatched != null)
            {
                SelectOnlineEpisodeCommand.Execute(nextUnwatched);
            }
        }
    }

    private AnimeEntity? _activeMatchedAnime;
    private bool _selectedEpisodeWasWatchedWhenSelected;

    public void OnAnimeProgressChanged(AnimeProgressChangedMessage message)
    {
        RefreshWatchingList();

        if (!IsOnlineEpisodeSelectorVisible || ActiveOnlineAnime == null)
            return;

        var targetAnime = _activeMatchedAnime ?? ResolveMatchedAnime(ActiveOnlineAnime.Title);
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
        if (!IsOnlineEpisodeSelectorVisible || ActiveOnlineAnime == null || message.Anime == null)
            return;

        if (IsAnimeTitleMatch(message.Anime, ActiveOnlineAnime.Title) ||
            OnlineRelevanceScorer.ComputeRelevance(message.Anime.Title, ActiveOnlineAnime.Title) >= 0.65 ||
            (!string.IsNullOrWhiteSpace(message.Anime.RussianTitle) && OnlineRelevanceScorer.ComputeRelevance(message.Anime.RussianTitle, ActiveOnlineAnime.Title) >= 0.65))
        {
            SetActiveMatchedAnime(message.Anime);
            UpdateEpisodeWatchedStates(message.Anime);
        }
    }

    public void OnAnimeListRefreshed()
    {
        RefreshWatchingList();

        if (IsOnlineEpisodeSelectorVisible && ActiveOnlineAnime != null)
        {
            var matched = ResolveMatchedAnime(ActiveOnlineAnime.Title);
            if (matched != null)
            {
                if (!ReferenceEquals(_activeMatchedAnime, matched))
                {
                    SetActiveMatchedAnime(matched);
                }
                UpdateEpisodeWatchedStates(matched);
            }
        }
    }

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

        var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(title);
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
