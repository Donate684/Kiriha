using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Utils.Async;
using Kiriha.Core.Streaming.Services;
using Kiriha.Models;
using Kiriha.Utils;
using Serilog;

namespace Kiriha.ViewModels.Torrents;

public partial class TorrentsViewModel
{
    private async Task PerformOnlineSearchAsync(string queryText, CancellationToken ct = default)
    {
        var query = queryText.Trim();
        if (string.IsNullOrEmpty(query))
        {
            if (SelectedAnime != null)
            {
                query = !string.IsNullOrWhiteSpace(SelectedAnime.RussianTitle)
                    ? SelectedAnime.RussianTitle
                    : SelectedAnime.Title;
            }
        }

        if (string.IsNullOrEmpty(query))
        {
            OnlineResults.Clear();
            return;
        }

        var enabledProviders = GetEnabledOnlineProviders();
        if (enabledProviders.Count == 0)
        {
            OnlineResults.Clear();
            return;
        }

        if (_streamingService == null)
        {
            Log.Warning("[Torrents] StreamingService is not registered");
            return;
        }

        if (ct == default)
        {
            if (_searchCts != null)
            {
                await _searchCts.CancelAsync();
                _searchCts.Dispose();
            }
            _searchCts = new CancellationTokenSource();
            ct = _searchCts.Token;
        }

        try
        {
            IsLoading = true;
            Log.Information("[Torrents.Online] Searching for '{Query}' with providers: {Providers}", query, string.Join(", ", enabledProviders));

            var searchTargets = new List<string> { query };
            if (SelectedAnime != null)
            {
                if (!string.IsNullOrWhiteSpace(SelectedAnime.RussianTitle))
                    searchTargets.Add(SelectedAnime.RussianTitle);
                if (!string.IsNullOrWhiteSpace(SelectedAnime.Title))
                    searchTargets.Add(SelectedAnime.Title);
                if (!string.IsNullOrWhiteSpace(SelectedAnime.EnglishTitle))
                    searchTargets.Add(SelectedAnime.EnglishTitle);
            }

            var allItems = new List<OnlineAnimeItem>();
            var hasAny = false;

            await foreach (var batch in _streamingService.SearchStreamAsync(query, enabledProviders, ct).ConfigureAwait(false))
            {
                if (ct.IsCancellationRequested) return;
                if (batch.Count == 0) continue;
                var newItems = batch.Select(r => new OnlineAnimeItem(r, OnlineRelevanceScorer.ComputeMaxRelevance(searchTargets, r.Title))).ToList();
                allItems.AddRange(newItems);

                var snapshot = allItems
                    .OrderByDescending(x => x.RelevanceScore)
                    .ThenBy(x => x.ProviderName)
                    .ToList();

                Dispatcher.UIThread.Post(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    OnlineResults.Reset(snapshot);
                    if (!hasAny)
                    {
                        hasAny = true;
                        IsLoading = false;
                    }
                });
            }

            Log.Information("[Torrents.Online] Search completed with {Count} items", allItems.Count);
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Torrents.Online] Search failed for '{Query}'", query);
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterDubSelected))]
    [NotifyPropertyChangedFor(nameof(IsFilterSubSelected))]
    private string _onlineSourceFilter = "dub";

    partial void OnOnlineSourceFilterChanged(string value)
    {
        ApplyOnlineSourceFilter();
    }

    public bool IsFilterDubSelected => OnlineSourceFilter == "dub";
    public bool IsFilterSubSelected => OnlineSourceFilter == "sub";

    public int DubSourcesCount => OnlineSources.Count(s => !s.IsSubtitles && (!Hide480pSources || !s.Is480pOrLower) && (!Hide720pSources || !s.Is720p) && (!HideNonRussianSources || !s.IsNonRussian));
    public int SubSourcesCount => OnlineSources.Count(s => s.IsSubtitles && (!Hide480pSources || !s.Is480pOrLower) && (!Hide720pSources || !s.Is720p) && (!HideNonRussianSources || !s.IsNonRussian));

    public bool HasDubSources => DubSourcesCount > 0;
    public bool HasSubSources => SubSourcesCount > 0;
    public bool HasNoDisplayedSources => DisplayedOnlineSources.Count == 0 && !IsOnlineSourcesLoading && OnlineSources.Count > 0;

    [RelayCommand]
    public void SetOnlineSourceFilter(string? filter)
    {
        var target = string.Equals(filter, "sub", StringComparison.OrdinalIgnoreCase) ? "sub" : "dub";
        OnlineSourceFilter = target;
        _settingsService.Update(s => s.Torrents.OnlineStreaming.PreferredSourceType = target, SettingsSection.Torrents);
    }

    private void ApplyOnlineSourceFilter()
    {
        IEnumerable<OnlineSourceItem> filtered = OnlineSourceFilter switch
        {
            "sub" => OnlineSources.Where(s => s.IsSubtitles),
            _ => OnlineSources.Where(s => !s.IsSubtitles)
        };

        if (Hide480pSources)
        {
            filtered = filtered.Where(s => !s.Is480pOrLower);
        }

        if (Hide720pSources)
        {
            filtered = filtered.Where(s => !s.Is720p);
        }

        if (HideNonRussianSources)
        {
            filtered = filtered.Where(s => !s.IsNonRussian);
        }

        var list = filtered.ToList();

        DisplayedOnlineSources.Clear();
        foreach (var item in list)
        {
            DisplayedOnlineSources.Add(item);
        }

        OnPropertyChanged(nameof(HasNoDisplayedSources));
    }

    private void NotifySourceCounts()
    {
        OnPropertyChanged(nameof(DubSourcesCount));
        OnPropertyChanged(nameof(SubSourcesCount));
        OnPropertyChanged(nameof(HasDubSources));
        OnPropertyChanged(nameof(HasSubSources));
    }
}
