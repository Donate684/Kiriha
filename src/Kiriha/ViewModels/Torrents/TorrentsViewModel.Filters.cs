using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.ViewModels.Torrents;

#pragma warning disable MVVMTK0034

public partial class TorrentsViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(PreviewQuery))]
    private bool _onlyCrunchyroll;

    partial void OnOnlyCrunchyrollChanged(bool value) => PersistFilter(nameof(OnlyCrunchyroll), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(PreviewQuery))]
    private bool _filterNetflix;

    partial void OnFilterNetflixChanged(bool value) => PersistFilter(nameof(FilterNetflix), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(PreviewQuery))]
    private bool _filterAmazon;

    partial void OnFilterAmazonChanged(bool value) => PersistFilter(nameof(FilterAmazon), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(PreviewQuery))]
    private bool _filterHidive;

    partial void OnFilterHidiveChanged(bool value) => PersistFilter(nameof(FilterHidive), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(PreviewQuery))]
    private bool _filterVaryg;

    partial void OnFilterVarygChanged(bool value) => PersistFilter(nameof(FilterVaryg), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(PreviewQuery))]
    private bool _filterEraiRaws;

    partial void OnFilterEraiRawsChanged(bool value) => PersistFilter(nameof(FilterEraiRaws), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(PreviewQuery))]
    private bool _filterToonsHub;

    partial void OnFilterToonsHubChanged(bool value) => PersistFilter(nameof(FilterToonsHub), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(PreviewQuery))]
    private bool _filterJudas;

    partial void OnFilterJudasChanged(bool value) => PersistFilter(nameof(FilterJudas), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(PreviewQuery))]
    private bool _filterHevc;

    partial void OnFilterHevcChanged(bool value) => PersistFilter(nameof(FilterHevc), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(PreviewQuery))]
    private bool _filter1080p;

    partial void OnFilter1080pChanged(bool value) => PersistFilter(nameof(Filter1080p), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(PreviewQuery))]
    private bool _useCustomQuery;

    partial void OnUseCustomQueryChanged(bool value)
    {
        if (_suppressFilterPersist) return;

        if (value)
        {
            if (SelectedAnime == null && _currentFilterContextId == 0 && !string.IsNullOrWhiteSpace(SearchQuery))
            {
                _customFilterAnchorQuery = SearchQuery.Trim();
                _currentFilterContextId = ResolveTitleIdFromQuery(_customFilterAnchorQuery);
            }

            if (string.IsNullOrWhiteSpace(CustomQuery))
            {
                _suppressFilterPersist = true;
                try
                {
                    CustomQuery = GetCurrentTitleDefaultQuery();
                }
                finally
                {
                    _suppressFilterPersist = false;
                }
            }

            if (!string.IsNullOrWhiteSpace(CustomQuery))
            {
                SetSearchQueryFromContext(CustomQuery);
            }
        }
        else
        {
            string restoredQuery;
            if (SelectedAnime != null)
            {
                restoredQuery = GetAnimeDefaultQuery(SelectedAnime);
            }
            else if (_currentFilterContextId == 0)
            {
                restoredQuery = string.Empty;
            }
            else if (!string.IsNullOrWhiteSpace(_customFilterAnchorQuery))
            {
                restoredQuery = _customFilterAnchorQuery;
            }
            else
            {
                restoredQuery = SearchQuery?.Trim() ?? string.Empty;
            }

            _suppressFilterPersist = true;
            try
            {
                CustomQuery = SelectedAnime != null ? SelectedAnime.Title : restoredQuery;
            }
            finally
            {
                _suppressFilterPersist = false;
            }

            SetSearchQueryFromContext(restoredQuery);
        }

        PersistCustomQuerySettings();
        PerformSearchCommand.Execute(null);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewQuery))]
    private string _customQuery = string.Empty;

    partial void OnCustomQueryChanged(string value)
    {
        if (_suppressFilterPersist) return;

        if (UseCustomQuery)
        {
            SetSearchQueryFromContext(value);
            PersistCustomQuerySettings();
        }
    }

    public string PreviewQuery => TorrentQueryBuilder.Build(
        UseCustomQuery && !string.IsNullOrWhiteSpace(CustomQuery) ? CustomQuery : SearchQuery,
        new TorrentQueryFilters(
            FilterVaryg,
            FilterEraiRaws,
            FilterToonsHub,
            FilterJudas,
            Filter1080p,
            FilterHevc,
            OnlyCrunchyroll,
            FilterNetflix,
            FilterAmazon,
            FilterHidive));

    [ObservableProperty]
    private bool _filtersPerTitle = true;

    /// <summary>Set during bulk-load of filter values so partial change handlers don't persist back.</summary>
    private bool _suppressFilterPersist;
    private bool _updatingSearchQueryFromContext;
    private int _currentFilterContextId;
    private string _customFilterAnchorQuery = string.Empty;

    private void SetSearchQueryFromContext(string query)
    {
        _updatingSearchQueryFromContext = true;
        try
        {
            SearchQuery = query;
        }
        finally
        {
            _updatingSearchQueryFromContext = false;
        }
    }

    private string GetAnimeDefaultQuery(AnimeEntity anime) =>
        IsOnlineMode && !string.IsNullOrWhiteSpace(anime.RussianTitle)
            ? anime.RussianTitle
            : anime.Title;

    public bool HasActiveFilters =>
        FilterVaryg || FilterEraiRaws || FilterToonsHub || FilterJudas || Filter1080p || FilterHevc
        || OnlyCrunchyroll || FilterNetflix || FilterAmazon || FilterHidive
        || FilterOnlyNew || FilterOnlyBatches
        || UseCustomQuery;

    partial void OnFiltersPerTitleChanged(bool value)
    {
        _settingsService.Update(settings => settings.Torrents.FiltersPerTitle = value, SettingsSection.Torrents);
        ReloadFiltersForCurrentContext();
    }

    private void LoadFilterSettings()
    {
        if (!_settingsService.Current.Torrents.FiltersPerTitle)
        {
            // Self-heal legacy pre-v1.3.35 configs where FiltersPerTitle=false had no UI toggle
            // and caused a stuck global CustomQuery to override per-title filters across all anime.
            _settingsService.Update(settings =>
            {
                settings.Torrents.FiltersPerTitle = true;
                settings.Torrents.UseCustomQuery = false;
                settings.Torrents.CustomQuery = null;
            }, SettingsSection.Torrents);
        }

        _filtersPerTitle = true;
        ReloadFiltersForCurrentContext();
    }

    public void SyncFilterContext()
    {
        var titleId = ResolveCurrentTitleId();
        if (titleId < 0 && !UseCustomQuery && !string.IsNullOrWhiteSpace(SearchQuery))
        {
            _customFilterAnchorQuery = SearchQuery.Trim();
        }
        else if (titleId >= 0)
        {
            _customFilterAnchorQuery = string.Empty;
        }

        if (titleId != _currentFilterContextId)
        {
            ReloadFiltersForCurrentContext();
        }
    }

    public int ResolveCurrentTitleId()
    {
        if (SelectedAnime != null)
        {
            return SelectedAnime.Id;
        }

        if (UseCustomQuery)
        {
            return _currentFilterContextId;
        }

        return ResolveTitleIdFromQuery(SearchQuery);
    }

    private int ResolveTitleIdFromQuery(string? rawQuery)
    {
        var query = rawQuery?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            return 0;
        }

        var matched = ResolveAnimeForQuery(query);
        if (matched != null)
        {
            return matched.Id;
        }

        return GetCustomTitleFilterId(query);
    }

    public static int GetCustomTitleFilterId(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return 0;
        var normalized = title.Trim().ToLowerInvariant();
        uint hash = 2166136261;
        foreach (char c in normalized)
        {
            hash ^= c;
            hash *= 16777619;
        }
        int id = (int)(hash & 0x7FFFFFFF);
        if (id == 0) id = 1;
        return -id;
    }

    private AnimeEntity? ResolveAnimeForQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        var trimmed = query.Trim();
        return _animeRepo.Collection
            .Where(a => a.MediaKind == MediaKind.Anime &&
                        (string.Equals(a.Title, trimmed, StringComparison.OrdinalIgnoreCase) ||
                         (!string.IsNullOrEmpty(a.EnglishTitle) && string.Equals(a.EnglishTitle, trimmed, StringComparison.OrdinalIgnoreCase)) ||
                         (!string.IsNullOrEmpty(a.RussianTitle) && string.Equals(a.RussianTitle, trimmed, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(a => a.Status switch
            {
                UserAnimeStatus.Watching => 5,
                UserAnimeStatus.PlanToWatch => 4,
                UserAnimeStatus.OnHold => 3,
                UserAnimeStatus.Completed => 2,
                UserAnimeStatus.Dropped => 1,
                _ => 0
            })
            .FirstOrDefault();
    }

    private string GetCurrentTitleDefaultQuery()
    {
        if (SelectedAnime != null) return SelectedAnime.Title;
        return SearchQuery?.Trim() ?? string.Empty;
    }

    private void PersistFilter(string name, bool value)
    {
        if (_suppressFilterPersist) return;

        var titleId = ResolveCurrentTitleId();
        _currentFilterContextId = titleId;

        if (titleId != 0)
        {
            var target = _torrentFilterRepo.TryGetCachedFilter(titleId)
                ?? CreateFilterSetFromGlobal(_settingsService.Current.Torrents, includeCustomQuery: false);
            ApplyFilterValue(target, name, value);
            _ = _torrentFilterRepo.SaveFilterAsync(titleId, target);
        }
        else
        {
            _settingsService.Update(settings =>
            {
                ApplyGlobalFilterValue(settings.Torrents, name, value);
            }, SettingsSection.Torrents);
        }

        if (name != nameof(FilterOnlyNew) && name != nameof(FilterOnlyBatches))
        {
            PerformSearchCommand.Execute(null);
        }
    }

    public void ReloadFiltersForCurrentContext()
    {
        var titleId = ResolveCurrentTitleId();
        _currentFilterContextId = titleId;
        if (titleId >= 0)
        {
            _customFilterAnchorQuery = string.Empty;
        }

        var globalCfg = _settingsService.Current.Torrents;
        AppSettings.TorrentFilterSet src;
        if (titleId != 0)
        {
            src = _torrentFilterRepo.TryGetCachedFilter(titleId)
                ?? CreateFilterSetFromGlobal(globalCfg, includeCustomQuery: false);
        }
        else
        {
            src = CreateFilterSetFromGlobal(globalCfg, includeCustomQuery: true);
        }

        _suppressFilterPersist = true;
        try
        {
            OnlyCrunchyroll = src.OnlyCrunchyroll;
            FilterNetflix = src.FilterNetflix;
            FilterAmazon = src.FilterAmazon;
            FilterHidive = src.FilterHidive;
            FilterVaryg = src.FilterVaryg;
            FilterEraiRaws = src.FilterEraiRaws;
            FilterToonsHub = src.FilterToonsHub;
            FilterJudas = src.FilterJudas;
            FilterHevc = src.FilterHevc;
            Filter1080p = src.Filter1080p;
            FilterOnlyNew = src.FilterOnlyNew;
            FilterOnlyBatches = src.FilterOnlyBatches;
            UseCustomQuery = src.UseCustomQuery;
            CustomQuery = src.UseCustomQuery && !string.IsNullOrWhiteSpace(src.CustomQuery)
                ? src.CustomQuery
                : GetCurrentTitleDefaultQuery();
        }
        finally
        {
            _suppressFilterPersist = false;
        }
    }

    private void PersistCustomQuerySettings()
    {
        if (_suppressFilterPersist) return;

        var titleId = SelectedAnime?.Id ?? _currentFilterContextId;
        _currentFilterContextId = titleId;
        var persistedCustomQuery = UseCustomQuery ? CustomQuery : null;

        if (titleId != 0)
        {
            var target = _torrentFilterRepo.TryGetCachedFilter(titleId)
                ?? CreateFilterSetFromGlobal(_settingsService.Current.Torrents, includeCustomQuery: false);
            target.UseCustomQuery = UseCustomQuery;
            target.CustomQuery = persistedCustomQuery;
            _ = _torrentFilterRepo.SaveFilterAsync(titleId, target);
        }
        else
        {
            _settingsService.Update(settings =>
            {
                settings.Torrents.UseCustomQuery = UseCustomQuery;
                settings.Torrents.CustomQuery = persistedCustomQuery;
            }, SettingsSection.Torrents);
        }
    }

    private static void ApplyFilterValue(AppSettings.TorrentFilterSet target, string name, bool value)
    {
        switch (name)
        {
            case nameof(OnlyCrunchyroll): target.OnlyCrunchyroll = value; break;
            case nameof(FilterNetflix): target.FilterNetflix = value; break;
            case nameof(FilterAmazon): target.FilterAmazon = value; break;
            case nameof(FilterHidive): target.FilterHidive = value; break;
            case nameof(FilterVaryg): target.FilterVaryg = value; break;
            case nameof(FilterEraiRaws): target.FilterEraiRaws = value; break;
            case nameof(FilterToonsHub): target.FilterToonsHub = value; break;
            case nameof(FilterJudas): target.FilterJudas = value; break;
            case nameof(FilterHevc): target.FilterHevc = value; break;
            case nameof(Filter1080p): target.Filter1080p = value; break;
            case nameof(FilterOnlyNew): target.FilterOnlyNew = value; break;
            case nameof(FilterOnlyBatches): target.FilterOnlyBatches = value; break;
        }
    }

    private static void ApplyGlobalFilterValue(AppSettings.TorrentConfig target, string name, bool value)
    {
        switch (name)
        {
            case nameof(OnlyCrunchyroll): target.OnlyCrunchyroll = value; break;
            case nameof(FilterNetflix): target.FilterNetflix = value; break;
            case nameof(FilterAmazon): target.FilterAmazon = value; break;
            case nameof(FilterHidive): target.FilterHidive = value; break;
            case nameof(FilterVaryg): target.FilterVaryg = value; break;
            case nameof(FilterEraiRaws): target.FilterEraiRaws = value; break;
            case nameof(FilterToonsHub): target.FilterToonsHub = value; break;
            case nameof(FilterJudas): target.FilterJudas = value; break;
            case nameof(FilterHevc): target.FilterHevc = value; break;
            case nameof(Filter1080p): target.Filter1080p = value; break;
            case nameof(FilterOnlyNew): target.FilterOnlyNew = value; break;
            case nameof(FilterOnlyBatches): target.FilterOnlyBatches = value; break;
        }
    }

    private static AppSettings.TorrentFilterSet CreateFilterSetFromGlobal(
        AppSettings.TorrentConfig cfg,
        bool includeCustomQuery = false) => new()
    {
        OnlyCrunchyroll = cfg.OnlyCrunchyroll,
        FilterNetflix = cfg.FilterNetflix,
        FilterAmazon = cfg.FilterAmazon,
        FilterHidive = cfg.FilterHidive,
        FilterVaryg = cfg.FilterVaryg,
        FilterEraiRaws = cfg.FilterEraiRaws,
        FilterToonsHub = cfg.FilterToonsHub,
        FilterJudas = cfg.FilterJudas,
        FilterHevc = cfg.FilterHevc,
        Filter1080p = cfg.Filter1080p,
        FilterOnlyNew = cfg.FilterOnlyNew,
        FilterOnlyBatches = cfg.FilterOnlyBatches,
        UseCustomQuery = includeCustomQuery && cfg.UseCustomQuery,
        CustomQuery = includeCustomQuery ? cfg.CustomQuery : null,
    };

    [RelayCommand]
    public void ClearFilters()
    {
        var titleId = ResolveCurrentTitleId();
        var wasUsingCustomQuery = UseCustomQuery;
        _currentFilterContextId = titleId;

        var defaultQuery = SelectedAnime != null
            ? GetAnimeDefaultQuery(SelectedAnime)
            : (titleId == 0
                ? string.Empty
                : (!string.IsNullOrWhiteSpace(_customFilterAnchorQuery)
                    ? _customFilterAnchorQuery
                    : (SearchQuery?.Trim() ?? string.Empty)));

        _suppressFilterPersist = true;
        try
        {
            FilterVaryg = false;
            FilterEraiRaws = false;
            FilterToonsHub = false;
            FilterJudas = false;
            Filter1080p = false;
            FilterHevc = false;
            OnlyCrunchyroll = false;
            FilterNetflix = false;
            FilterAmazon = false;
            FilterHidive = false;
            FilterOnlyNew = false;
            FilterOnlyBatches = false;
            UseCustomQuery = false;
            CustomQuery = SelectedAnime != null ? SelectedAnime.Title : defaultQuery;
        }
        finally
        {
            _suppressFilterPersist = false;
        }

        if (wasUsingCustomQuery)
        {
            SetSearchQueryFromContext(defaultQuery);
        }

        if (titleId != 0)
        {
            var target = _torrentFilterRepo.TryGetCachedFilter(titleId) ?? new AppSettings.TorrentFilterSet();
            target.FilterVaryg = false;
            target.FilterEraiRaws = false;
            target.FilterToonsHub = false;
            target.FilterJudas = false;
            target.Filter1080p = false;
            target.FilterHevc = false;
            target.OnlyCrunchyroll = false;
            target.FilterNetflix = false;
            target.FilterAmazon = false;
            target.FilterHidive = false;
            target.FilterOnlyNew = false;
            target.FilterOnlyBatches = false;
            target.UseCustomQuery = false;
            target.CustomQuery = null;
            _ = _torrentFilterRepo.SaveFilterAsync(titleId, target);
        }
        else
        {
            _settingsService.Update(settings =>
            {
                var cfg = settings.Torrents;
                cfg.FilterVaryg = false;
                cfg.FilterEraiRaws = false;
                cfg.FilterToonsHub = false;
                cfg.FilterJudas = false;
                cfg.Filter1080p = false;
                cfg.FilterHevc = false;
                cfg.OnlyCrunchyroll = false;
                cfg.FilterNetflix = false;
                cfg.FilterAmazon = false;
                cfg.FilterHidive = false;
                cfg.FilterOnlyNew = false;
                cfg.FilterOnlyBatches = false;
                cfg.UseCustomQuery = false;
                cfg.CustomQuery = null;
            }, SettingsSection.Torrents);
        }

        RebuildGroupedTorrents();
        PerformSearchCommand.Execute(null);
    }
}

#pragma warning restore MVVMTK0034
