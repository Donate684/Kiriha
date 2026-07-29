using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Models;
using Kiriha.ViewModels.Settings;
using Serilog;

namespace Kiriha.ViewModels.Search;

public partial class SearchViewModel
{
    partial void OnSearchQueryChanged(string value) => TriggerSearch();
    partial void OnHideInListsChanged(bool value) => TriggerSearch();
    partial void OnAdultFilterChanged(AdultFilterMode value) => TriggerSearch();

    private void TriggerSearch()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) || SearchQuery.Length < 3)
        {
            var cts = Interlocked.Exchange(ref _searchCts, null);
            if (cts != null)
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
                cts.Dispose();
            }
            SearchResults.Clear();
            return;
        }
        _searchDebouncer.Invoke();
    }

    [RelayCommand]
    public async Task PerformSearch()
    {
        if (_isDisposed || string.IsNullOrWhiteSpace(SearchQuery)) return;

        var newCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _searchCts, newCts);

        if (oldCts != null)
        {
            try { oldCts.Cancel(); } catch (ObjectDisposedException) { }
            oldCts.Dispose();
        }

        var ct = newCts.Token;

        IsLoading = true;
        SearchResults.Clear();

        try
        {
            string actualQuery = SearchQuery;
            if (actualQuery.Any(c => c >= '\u0400' && c <= '\u04FF'))
            {
                var englishName = await _shikiMetadataService.ResolveRussianQueryAsync(actualQuery, ct);
                if (!string.IsNullOrEmpty(englishName))
                {
                    actualQuery = englishName;
                }
            }

            var results = await _apiService.SearchAnimeAsync(actualQuery, ct);
            if (results.Any())
            {
                System.Collections.Generic.IEnumerable<AnimeItem> filtered = results;

                // Adult filtering
                if (AdultFilter == AdultFilterMode.Hide)
                {
                    // Strict safe mode: hide Rx rating and Hentai genre
                    filtered = filtered.Where(x =>
                        !string.Equals(x.Rating, "rx", StringComparison.OrdinalIgnoreCase) &&
                        !x.Genres.Any(g => string.Equals(g, "Hentai", StringComparison.OrdinalIgnoreCase)));
                }
                else if (AdultFilter == AdultFilterMode.Only)
                {
                    // Adult only: show only Rx rating or Hentai genre
                    filtered = filtered.Where(x =>
                        string.Equals(x.Rating, "rx", StringComparison.OrdinalIgnoreCase) ||
                        x.Genres.Any(g => string.Equals(g, "Hentai", StringComparison.OrdinalIgnoreCase)));
                }

                // Hide in lists filtering
                if (HideInLists)
                {
                    filtered = filtered.Where(x => x.Status == Models.Entities.UserAnimeStatus.None);
                }

                SearchResults.Reset(filtered);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error(ex, "Search failed");
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }
}
