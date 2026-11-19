using CommunityToolkit.Mvvm.ComponentModel;
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
            _ = _torrentFilterRepo.SaveFilterAsync(titleId, target, CancellationToken.None);
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
}

#pragma warning restore MVVMTK0034
