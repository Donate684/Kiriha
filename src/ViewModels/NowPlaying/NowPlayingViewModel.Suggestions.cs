using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Core;
using Kiriha.Core.Platform;
using Serilog;

namespace Kiriha.ViewModels.NowPlaying;

public partial class NowPlayingViewModel
{
    [RelayCommand]
    private async Task SelectSuggestion(object parameter)
    {
        if (parameter is not AnimeItem suggestion) return;

        Log.Information("Selecting anime suggestion: {Title} (ID: {Id})", suggestion.Title, suggestion.Id);
        LogDetection(CurrentMedia ?? new Kiriha.Models.ParsedMedia { AnimeTitle = suggestion.Title }, UIUtils.GetLoc("scrobbler.status.mapped_by") + " " + suggestion.Presentation.DisplayTitle);

        Volatile.Write(ref _pendingManualMatchId, suggestion.Id);
        ShowSuggestions = false;
        Suggestions.Clear();
        OnPropertyChanged(nameof(HasSuggestions));

        try
        {
            MatchedAnime = suggestion;
            IsManuallyMapped = true;
            await _trackingService.ManualMapAsync(suggestion.Id);
            MatchedAnime = suggestion;
            IsManuallyMapped = true;
        }
        catch
        {
            Volatile.Write(ref _pendingManualMatchId, 0);
            throw;
        }
    }

    [RelayCommand]
    private void DismissSuggestions()
    {
        ShowSuggestions = false;
        Suggestions.Clear();
        OnPropertyChanged(nameof(HasSuggestions));
    }

    [RelayCommand]
    private async Task SearchSuggestions()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;

        var cts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _searchCts, cts);
        try { oldCts?.Cancel(); } catch (Exception ex) { Log.Debug(ex, "Error canceling search CTS"); }
        oldCts?.Dispose();

        IsSearching = true;
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, _disposeCts.Token);
            var results = await _malApi.SearchAnimeAsync(SearchQuery, linkedCts.Token);
            if (linkedCts.Token.IsCancellationRequested) return;

            Suggestions.Clear();

            foreach (var r in results)
            {
                Suggestions.Add(r);
            }

            ShowSuggestions = Suggestions.Count > 0;
            OnPropertyChanged(nameof(HasSuggestions));
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to search anime inline");
        }
        finally
        {
            if (_searchCts == cts)
                IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task OpenSearchPanel()
    {
        IsSearchPanelOpen = true;
        if (string.IsNullOrWhiteSpace(SearchQuery) && CurrentMedia != null)
            SearchQuery = CurrentMedia.AnimeTitle;
        if (Suggestions.Count == 0 && !string.IsNullOrWhiteSpace(SearchQuery))
            await SearchSuggestions();
    }

    [RelayCommand]
    private void CloseSearchPanel()
    {
        IsSearchPanelOpen = false;
    }
}
