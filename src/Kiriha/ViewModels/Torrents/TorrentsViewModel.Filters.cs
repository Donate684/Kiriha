using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;

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

    public bool HasActiveFilters =>
        FilterVaryg || FilterEraiRaws || FilterToonsHub || FilterJudas || Filter1080p || FilterHevc
        || OnlyCrunchyroll || FilterNetflix || FilterAmazon || FilterHidive
        || FilterOnlyNew || FilterOnlyBatches
        || UseCustomQuery;

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
            _ = _torrentFilterRepo.SaveFilterAsync(titleId, target, CancellationToken.None);
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
            _ = _torrentFilterRepo.SaveFilterAsync(titleId, target, CancellationToken.None);
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
