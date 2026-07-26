using Kiriha.ViewModels.Analytics;
using Kiriha.ViewModels.AnimeList;
using Kiriha.ViewModels.History;
using Kiriha.ViewModels.NowPlaying;
using Kiriha.ViewModels.Search;
using Kiriha.ViewModels.Seasonal;
using Kiriha.ViewModels.Settings;
using Kiriha.ViewModels.Torrents;
using Kiriha.ViewModels.Startup;
using Kiriha.ViewModels.Dialogs;
using System;
using System.Collections.Generic;

namespace Kiriha.ViewModels.Main;

public partial class MainWindowViewModel
{
    private readonly HashSet<ViewModelBase> _cachedVms = new();

    private AnimeListViewModel? _animeListViewModel;
    private SettingsViewModel? _settingsViewModel;
    private NowPlayingViewModel? _nowPlayingViewModel;
    private HistoryViewModel? _historyViewModel;
    private TorrentsViewModel? _torrentsViewModel;
    private SeasonalViewModel? _seasonalViewModel;
    private AnalyticsViewModel? _analyticsViewModel;

    private void SetCurrentPage(ViewModelBase page)
    {
        if (CurrentPage is IDisposable disposable && !_cachedVms.Contains(CurrentPage))
        {
            disposable.Dispose();
        }
        CurrentPage = page;
    }

    private T EnsureCachedViewModel<T>(ref T? backingField) where T : ViewModelBase
    {
        if (backingField == null)
        {
            backingField = _viewModelFactory.Create<T>();
            _cachedVms.Add(backingField);
        }
        return backingField;
    }

    private SettingsViewModel EnsureSettingsViewModel()
    {
        if (_settingsViewModel != null)
            return _settingsViewModel;

        _settingsViewModel = _viewModelFactory.Create<SettingsViewModel>();
        _cachedVms.Add(_settingsViewModel);
        OnPropertyChanged(nameof(SettingsViewModel));
        return _settingsViewModel;
    }

    private AnimeListViewModel EnsureAnimeListViewModel() =>
        EnsureCachedViewModel(ref _animeListViewModel);

    private NowPlayingViewModel EnsureNowPlayingViewModel() =>
        EnsureCachedViewModel(ref _nowPlayingViewModel);

    private HistoryViewModel EnsureHistoryViewModel() =>
        EnsureCachedViewModel(ref _historyViewModel);

    private TorrentsViewModel EnsureTorrentsViewModel() =>
        EnsureCachedViewModel(ref _torrentsViewModel);

    private SeasonalViewModel EnsureSeasonalViewModel() =>
        EnsureCachedViewModel(ref _seasonalViewModel);

    private AnalyticsViewModel EnsureAnalyticsViewModel() =>
        EnsureCachedViewModel(ref _analyticsViewModel);
}
