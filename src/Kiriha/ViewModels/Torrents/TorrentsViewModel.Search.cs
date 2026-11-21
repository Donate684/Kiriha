using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.ViewModels.Torrents;

public partial class TorrentsViewModel
{
    partial void OnSelectedAnimeChanged(AnimeEntity? value)
    {
        foreach (var item in HideMenuItems)
        {
            item.IsSelected = value != null && item.Anime.Id == value.Id;
        }

        ReloadFiltersForCurrentContext();

        if (value != null)
        {
            var nextQuery = !IsOnlineMode && UseCustomQuery && !string.IsNullOrWhiteSpace(CustomQuery)
                ? CustomQuery
                : GetAnimeDefaultQuery(value);
            SetSearchQueryFromContext(nextQuery);
            PerformSearchCommand.Execute(null);
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (_updatingSearchQueryFromContext) return;

        if (SelectedAnime != null)
        {
            bool isSameAsAnime = string.Equals(value, SelectedAnime.Title, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(SelectedAnime.EnglishTitle) && string.Equals(value, SelectedAnime.EnglishTitle, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(SelectedAnime.RussianTitle) && string.Equals(value, SelectedAnime.RussianTitle, StringComparison.OrdinalIgnoreCase))
                || (UseCustomQuery && !string.IsNullOrEmpty(CustomQuery) && string.Equals(value, CustomQuery, StringComparison.OrdinalIgnoreCase));

            if (!isSameAsAnime)
            {
                _selectedAnime = null;
                OnPropertyChanged(nameof(SelectedAnime));
                OnPropertyChanged(nameof(HasSelectedAnime));
                foreach (var item in HideMenuItems)
                {
                    item.IsSelected = false;
                }

                _suppressFilterPersist = true;
                try
                {
                    UseCustomQuery = false;
                }
                finally
                {
                    _suppressFilterPersist = false;
                }

                var matched = ResolveAnimeForQuery(value);
                if (matched != null)
                {
                    _selectedAnime = matched;
                    OnPropertyChanged(nameof(SelectedAnime));
                    OnPropertyChanged(nameof(HasSelectedAnime));
                    foreach (var item in HideMenuItems)
                    {
                        item.IsSelected = item.Anime.Id == matched.Id;
                    }
                }

                ReloadFiltersForCurrentContext();
            }
        }
        else if (!string.IsNullOrWhiteSpace(value))
        {
            var matched = ResolveAnimeForQuery(value);
            if (matched != null)
            {
                _selectedAnime = matched;
                OnPropertyChanged(nameof(SelectedAnime));
                OnPropertyChanged(nameof(HasSelectedAnime));
                foreach (var item in HideMenuItems)
                {
                    item.IsSelected = item.Anime.Id == matched.Id;
                }
                ReloadFiltersForCurrentContext();
            }
            else if (UseCustomQuery && !string.Equals(value, CustomQuery, StringComparison.OrdinalIgnoreCase))
            {
                _suppressFilterPersist = true;
                try
                {
                    UseCustomQuery = false;
                }
                finally
                {
                    _suppressFilterPersist = false;
                }
                ReloadFiltersForCurrentContext();
            }
        }
        else if (_currentFilterContextId != 0)
        {
            _suppressFilterPersist = true;
            try
            {
                UseCustomQuery = false;
            }
            finally
            {
                _suppressFilterPersist = false;
            }
            ReloadFiltersForCurrentContext();
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task PerformSearch()
    {
        var previousSearch = _searchCts;
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        if (previousSearch != null)
        {
            await previousSearch.CancelAsync();
            previousSearch.Dispose();
        }
        if (ct.IsCancellationRequested) return;

        if (IsOnlineMode)
        {
            await PerformOnlineSearchAsync(SearchQuery, ct);
            return;
        }

        SyncFilterContext();

        var baseQuery = UseCustomQuery && !string.IsNullOrWhiteSpace(CustomQuery)
            ? CustomQuery
            : SearchQuery;

        string query = TorrentQueryBuilder.Build(baseQuery, new TorrentQueryFilters(
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

        if (string.IsNullOrEmpty(query))
        {
            Log.Debug("Torrents: Query is empty, clearing results");
            Torrents.Clear();
            IsLoading = false;
            return;
        }

        try
        {
            IsLoading = true;
            Log.Information("Torrents: Starting search for: {Query}", query);

            var results = await _rssService.SearchTorrentsAsync(query, ct);
            if (ct.IsCancellationRequested) return;

            ApplySelectedAnimeMatches(results);

            Log.Information("Torrents: Search returned {Count} items", results.Count);

            Torrents.Reset(results);

            if (results.Count == 0)
            {
                Log.Warning("Torrents: No results found for: {Query}", query);
            }
        }
        catch (OperationCanceledException) { }
        catch (System.Exception ex)
        {
            Log.Error(ex, "Torrents: Search failed for: {Query}", query);
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
                Log.Debug("Torrents: Search process completed (loading set to false)");
            }
        }
    }

    private void ApplySelectedAnimeMatches(List<TorrentEntity> results)
    {
        if (SelectedAnime is not { Status: UserAnimeStatus.Watching or UserAnimeStatus.PlanToWatch } selected)
            return;

        if (!UseCustomQuery || string.IsNullOrWhiteSpace(CustomQuery))
            return;

        var normCustom = NormalizeTitle(CustomQuery);
        if (string.IsNullOrEmpty(normCustom))
            return;

        foreach (var torrent in results)
        {
            if (torrent.IsMatched || torrent.IsBatch)
                continue;

            if (!int.TryParse(torrent.Episode, out var epNum) || epNum <= selected.Progress)
                continue;

            var normTorrent = NormalizeTitle(torrent.AnimeTitle);
            if (!string.IsNullOrEmpty(normTorrent) &&
                (normTorrent.StartsWith(normCustom, StringComparison.Ordinal) ||
                 normCustom.StartsWith(normTorrent, StringComparison.Ordinal)))
            {
                torrent.IsMatched = true;
            }
        }
    }

    [RelayCommand]
    public void ClearSearch()
    {
        SelectedAnime = null;
        _currentFilterContextId = 0;
        _customFilterAnchorQuery = string.Empty;
        SetSearchQueryFromContext(string.Empty);
        ReloadFiltersForCurrentContext();
    }

    [RelayCommand]
    public void SelectAnime(AnimeEntity? anime)
    {
        if (SelectedAnime == anime && anime != null)
        {
            var nextQuery = !IsOnlineMode && UseCustomQuery && !string.IsNullOrWhiteSpace(CustomQuery)
                ? CustomQuery
                : GetAnimeDefaultQuery(anime);
            SetSearchQueryFromContext(nextQuery);
            PerformSearchCommand.Execute(null);
            return;
        }
        SelectedAnime = anime;
    }

    [RelayCommand]
    public void ClearSelectedAnime()
    {
        SelectedAnime = null;
        _currentFilterContextId = 0;
        _customFilterAnchorQuery = string.Empty;
        SetSearchQueryFromContext(string.Empty);
        ReloadFiltersForCurrentContext();
        Torrents.Clear();
        OnlineResults.Clear();
        RebuildGroupedTorrents();
    }
}

internal readonly record struct TorrentQueryFilters(
    bool FilterVaryg,
    bool FilterEraiRaws,
    bool FilterToonsHub,
    bool FilterJudas,
    bool Filter1080p,
    bool FilterHevc,
    bool OnlyCrunchyroll,
    bool FilterNetflix,
    bool FilterAmazon,
    bool FilterHidive);

internal static class TorrentQueryBuilder
{
    public static string Build(string? baseQuery, TorrentQueryFilters filters)
    {
        var query = baseQuery?.Trim() ?? string.Empty;

        query = Append(query, filters.FilterVaryg, "VARYG");
        query = Append(query, filters.FilterEraiRaws, "Erai-raws");
        query = Append(query, filters.FilterToonsHub, "ToonsHub");
        query = Append(query, filters.FilterJudas, "Judas");
        query = Append(query, filters.Filter1080p, "1080p");
        query = Append(query, filters.FilterHevc, "HEVC");
        query = Append(query, filters.OnlyCrunchyroll, "CR");
        query = Append(query, filters.FilterNetflix, "NF");
        query = Append(query, filters.FilterAmazon, "AMZN");
        query = Append(query, filters.FilterHidive, "HIDIVE");

        return query;
    }

    private static string Append(string query, bool enabled, string token)
    {
        if (!enabled) return query;
        return string.IsNullOrEmpty(query) ? token : $"{query} {token}";
    }
}
