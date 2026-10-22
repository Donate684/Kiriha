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
    [NotifyPropertyChangedFor(nameof(PreviewQuery))]
    private bool _useCustomQuery;

    partial void OnUseCustomQueryChanged(bool value)
    {
        if (_suppressFilterPersist) return;

        if (value)
        {
            if (string.IsNullOrWhiteSpace(CustomQuery))
            {
                CustomQuery = GetCurrentTitleDefaultQuery();
            }
            if (!string.IsNullOrWhiteSpace(CustomQuery))
            {
                SearchQuery = CustomQuery;
            }
        }
        else
        {
            var defaultQuery = GetCurrentTitleDefaultQuery();
            if (!string.IsNullOrWhiteSpace(defaultQuery))
            {
                SearchQuery = defaultQuery;
            }
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
            SearchQuery = value;
            PersistCustomQuerySettings();
        }
    }

    public string PreviewQuery => TorrentQueryBuilder.Build(
        SearchQuery,
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
    private bool _filtersPerTitle;

    /// <summary>Set during bulk-load of filter values so partial change handlers don't persist back.</summary>
    private bool _suppressFilterPersist;
    private int _currentFilterContextId;

    public bool HasActiveFilters =>
        FilterVaryg || FilterEraiRaws || FilterToonsHub || FilterJudas || Filter1080p || FilterHevc
        || OnlyCrunchyroll || FilterNetflix || FilterAmazon || FilterHidive
        || FilterOnlyNew || FilterOnlyBatches;

    partial void OnFiltersPerTitleChanged(bool value)
    {
        _settingsService.Update(settings => settings.Torrents.FiltersPerTitle = value, SettingsSection.Torrents);
        ReloadFiltersForCurrentContext();
    }

    private void LoadFilterSettings()
    {
        _filtersPerTitle = _settingsService.Current.Torrents.FiltersPerTitle;
        ReloadFiltersForCurrentContext();
    }

    public void SyncFilterContext()
    {
        var titleId = ResolveCurrentTitleId();
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

        var query = SearchQuery?.Trim();
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
        return _animeRepo.Collection.FirstOrDefault(a =>
            string.Equals(a.Title, trimmed, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(a.EnglishTitle) && string.Equals(a.EnglishTitle, trimmed, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(a.RussianTitle) && string.Equals(a.RussianTitle, trimmed, StringComparison.OrdinalIgnoreCase)));
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
        if (titleId != 0)
        {
            _currentFilterContextId = titleId;
            var target = _torrentFilterRepo.TryGetCachedFilter(titleId) ?? CreateFilterSetFromGlobal(_settingsService.Current.Torrents);
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

        var globalCfg = _settingsService.Current.Torrents;
        AppSettings.TorrentFilterSet src;
        if (titleId != 0)
        {
            src = _torrentFilterRepo.TryGetCachedFilter(titleId) ?? CreateFilterSetFromGlobal(globalCfg);
        }
        else
        {
            src = CreateFilterSetFromGlobal(globalCfg);
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
            CustomQuery = !string.IsNullOrWhiteSpace(src.CustomQuery)
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

        var titleId = ResolveCurrentTitleId();
        if (titleId != 0)
        {
            _currentFilterContextId = titleId;
            var target = _torrentFilterRepo.TryGetCachedFilter(titleId) ?? CreateFilterSetFromGlobal(_settingsService.Current.Torrents);
            target.UseCustomQuery = UseCustomQuery;
            target.CustomQuery = CustomQuery;
            _ = _torrentFilterRepo.SaveFilterAsync(titleId, target);
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

    private static AppSettings.TorrentFilterSet CreateFilterSetFromGlobal(AppSettings.TorrentConfig cfg) => new()
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
        UseCustomQuery = false,
        CustomQuery = null,
    };

    [RelayCommand]
    public void ClearFilters()
    {
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
        }
        finally
        {
            _suppressFilterPersist = false;
        }

        var titleId = ResolveCurrentTitleId();
        if (titleId != 0)
        {
            _currentFilterContextId = titleId;
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
            }, SettingsSection.Torrents);
        }

        RebuildGroupedTorrents();
        PerformSearchCommand.Execute(null);
    }
}

#pragma warning restore MVVMTK0034
