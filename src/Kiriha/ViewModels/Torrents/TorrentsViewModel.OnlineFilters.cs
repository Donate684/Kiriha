using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Streaming.Constants;
using Kiriha.Models;
using Kiriha.ViewModels.Settings;

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
                Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(FilterOnlineAnime365)));
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
    [NotifyPropertyChangedFor(nameof(ShowAnime365Option))]
    private bool _isAnime365LoggedIn;

    [ObservableProperty]
    private bool _showAnime365Warning;

    public bool ShowAnime365Option => IsAnime365LoggedIn;

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

    public int TotalAvailableOnlineSourcesCount => IsAnime365LoggedIn ? 5 : 4;

    public bool HasActiveOnlineSources => ActiveOnlineSourcesCount > 0;

    public int ActiveOnlineSourcesCount
    {
        get
        {
            var count = 0;
            if (FilterOnlineAniLibria) count++;
            if (FilterOnlineAnimeLib) count++;
            if (FilterOnlineDreamCast) count++;
            if (FilterOnlineYummyAnime) count++;
            if (FilterOnlineAnime365) count++;
            return count;
        }
    }

    public string ActiveOnlineSourcesCountText => $"{ActiveOnlineSourcesCount}/{TotalAvailableOnlineSourcesCount}";

    public bool AllOnlineSourcesActive => ActiveOnlineSourcesCount == TotalAvailableOnlineSourcesCount;

    public bool HasFilteredOnlineSources => ActiveOnlineSourcesCount > 0 && ActiveOnlineSourcesCount < TotalAvailableOnlineSourcesCount;

    [ObservableProperty]
    private bool _hide480pSources;

    partial void OnHide480pSourcesChanged(bool value)
    {
        PersistOnlineFilters();
        ApplyOnlineSourceFilter();
    }

    [ObservableProperty]
    private bool _hide720pSources;

    partial void OnHide720pSourcesChanged(bool value)
    {
        PersistOnlineFilters();
        ApplyOnlineSourceFilter();
    }

    [ObservableProperty]
    private bool _hideNonRussianSources;

    partial void OnHideNonRussianSourcesChanged(bool value)
    {
        PersistOnlineFilters();
        ApplyOnlineSourceFilter();
    }

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
            FilterOnlineAnime365 = IsAnime365LoggedIn;
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
    public void ClearAllOnlineSources() => DeselectAllOnlineSources();

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
}
