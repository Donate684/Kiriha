using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Infrastructure;
using Kiriha.Infrastructure.Tracking.Integration;
using Kiriha.Models;
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
            if (UseCustomQuery && !string.IsNullOrWhiteSpace(CustomQuery))
            {
                SearchQuery = CustomQuery;
            }
            else
            {
                SearchQuery = value.Title;
            }
            PerformSearchCommand.Execute(null);
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (SelectedAnime != null)
        {
            bool isSameAsAnime = string.Equals(value, SelectedAnime.Title, System.StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(SelectedAnime.EnglishTitle) && string.Equals(value, SelectedAnime.EnglishTitle, System.StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(SelectedAnime.RussianTitle) && string.Equals(value, SelectedAnime.RussianTitle, System.StringComparison.OrdinalIgnoreCase))
                || (UseCustomQuery && !string.IsNullOrEmpty(CustomQuery) && string.Equals(value, CustomQuery, System.StringComparison.OrdinalIgnoreCase));

            if (!isSameAsAnime)
            {
                _selectedAnime = null;
                OnPropertyChanged(nameof(SelectedAnime));
                foreach (var item in HideMenuItems)
                {
                    item.IsSelected = false;
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(value))
        {
            var matched = ResolveAnimeForQuery(value);
            if (matched != null)
            {
                _selectedAnime = matched;
                OnPropertyChanged(nameof(SelectedAnime));
                foreach (var item in HideMenuItems)
                {
                    item.IsSelected = item.Anime.Id == matched.Id;
                }
            }
        }
    }

    [RelayCommand]
    public async Task PerformSearch()
    {
        SyncFilterContext();

        string query = TorrentQueryBuilder.Build(SearchQuery, new TorrentQueryFilters(
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
            return;
        }

        try
        {
            IsLoading = true;
            Log.Information("Torrents: Starting search for: {Query}", query);

            var results = await _rssService.SearchTorrentsAsync(query);
            Log.Information("Torrents: Search returned {Count} items", results.Count);

            Torrents.Clear();
            foreach (var r in results) Torrents.Add(r);
            RebuildGroupedTorrents();

            if (!results.Any())
            {
                Log.Warning("Torrents: No results found for: {Query}", query);
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "Torrents: Search failed for: {Query}", query);
        }
        finally
        {
            IsLoading = false;
            Log.Debug("Torrents: Search process completed (loading set to false)");
        }
    }

    [RelayCommand]
    public void SelectAnime(AnimeEntity? anime)
    {
        if (SelectedAnime == anime && anime != null)
        {
            SearchQuery = UseCustomQuery && !string.IsNullOrWhiteSpace(CustomQuery)
                ? CustomQuery
                : anime.Title;
            PerformSearchCommand.Execute(null);
            return;
        }
        SelectedAnime = anime;
    }

    [RelayCommand]
    public void DownloadMagnet(TorrentEntity torrent)
    {
        if (torrent is null || string.IsNullOrEmpty(torrent.MagnetLink)) return;
        UIUtils.OpenUrl(torrent.MagnetLink);
    }

    [RelayCommand]
    public void DownloadTorrentFile(TorrentEntity torrent)
    {
        if (torrent is null || string.IsNullOrEmpty(torrent.DownloadLink)) return;
        UIUtils.OpenUrl(torrent.DownloadLink);
    }

    [RelayCommand]
    public void Refresh()
    {
        // RssFeedService checks automatically, but we could trigger a manual check here if needed.
    }

    [RelayCommand]
    public void ClearSelectedAnime()
    {
        SelectedAnime = null;
        SearchQuery = string.Empty;
        Torrents.Clear();
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


