using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Models;
using Kiriha.Utils.Async;
using Kiriha.ViewModels.Settings;
using Serilog;

namespace Kiriha.ViewModels.NowPlaying;

public partial class NowPlayingViewModel
{
    private readonly ConcurrentDictionary<int, byte> _detailsLoadingIds = new();

    /// <summary>
    /// Resolved user-defined share buttons for the currently matched anime.
    /// Refreshed via <see cref="OnMatchedAnimeChanged"/>.
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<CustomShareLinkRuntime> CustomShareLinks { get; } = new();

    private IReadOnlyList<string> _allAlternativeTitles = [];
    public IEnumerable<string> AllAlternativeTitles => _allAlternativeTitles;

    public bool HasAlternativeTitles => AllAlternativeTitles.Any();

    partial void OnMatchedAnimeChanged(AnimeEntity? value)
    {
        UpdateAlternativeTitles(value);
        UpdateCustomShareLinks(value);

        if (value is null) return;

        if (value.IsMissingDetails)
        {
            EnsureFullDetailsSafeAsync(value).SafeFireAndForget("NowPlaying.OnMatchedAnimeChanged");
        }
        else
        {
            EnsureLocalizedSafeAsync(value).SafeFireAndForget("NowPlaying.OnMatchedAnimeChangedLoc");
        }
    }

    public async Task EnsureFullDetailsSafeAsync(AnimeEntity anime)
    {
        if (anime is null || anime.Id <= 0) return;
        if (!anime.IsMissingDetails) return;

        int animeId = anime.Id;
        if (!_detailsLoadingIds.TryAdd(animeId, 0)) return;

        IsLoadingDetails = true;
        try
        {
            Log.Information("NowPlaying: Anime {Id} ({Title}) is missing details, requesting full data...", animeId, anime.Title);

            var full = anime.Presentation.IsManga
                ? await _malApi.GetMangaDetailsAsync(animeId, _disposeCts.Token)
                : await _malApi.GetAnimeDetailsAsync(animeId, _disposeCts.Token);

            if (full != null && MatchedAnime?.Id == animeId)
            {
                MatchedAnime.MergeFullDetails(full);

                UpdateAlternativeTitles(MatchedAnime);
                UpdateCustomShareLinks(MatchedAnime);

                await EnsureLocalizedSafeAsync(MatchedAnime);
                _trackingService.NotifyCurrentMediaMetadataUpdated(MatchedAnime);

                if (MatchedAnime.Status != UserAnimeStatus.None)
                {
                    try { await _animeRepo.AddOrUpdateAnimeAsync(MatchedAnime); }
                    catch (Exception ex) { Log.Debug(ex, "NowPlaying: Failed to persist enriched details for {Id}", animeId); }
                }

                OnPropertyChanged(nameof(MatchedAnime));
                OnPropertyChanged(nameof(AllAlternativeTitles));
                OnPropertyChanged(nameof(HasAlternativeTitles));
                OnPropertyChanged(nameof(DisplayEpisodeNumber));
                OnPropertyChanged(nameof(IsNotInList));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warning(ex, "NowPlaying: Failed to fetch full details for anime {Id}", animeId);
        }
        finally
        {
            _detailsLoadingIds.TryRemove(animeId, out _);
            IsLoadingDetails = false;
        }
    }

    private void UpdateAlternativeTitles(AnimeEntity? value)
    {
        if (value is null)
        {
            _allAlternativeTitles = [];
            OnPropertyChanged(nameof(AllAlternativeTitles));
            OnPropertyChanged(nameof(HasAlternativeTitles));
            return;
        }

        var list = new List<string>();
        if (!string.IsNullOrEmpty(value.EnglishTitle) && value.EnglishTitle != value.Title)
            list.Add(value.EnglishTitle);
        if (!string.IsNullOrEmpty(value.JapaneseTitle) && value.JapaneseTitle != value.Title)
            list.Add(value.JapaneseTitle);

        foreach (var syn in value.AlternativeTitles)
        {
            if (syn != value.Title && !list.Contains(syn))
                list.Add(syn);
        }
        _allAlternativeTitles = list;
        OnPropertyChanged(nameof(AllAlternativeTitles));
        OnPropertyChanged(nameof(HasAlternativeTitles));
    }

    private void UpdateCustomShareLinks(AnimeEntity? value)
    {
        CustomShareLinks.Clear();
        if (value is null) return;

        foreach (var link in _settingsService.Current.CustomLinks)
        {
            if (string.IsNullOrWhiteSpace(link.UrlTemplate)) continue;
            var url = Kiriha.Core.CustomLinkResolver.Resolve(link.UrlTemplate, value);
            CustomShareLinks.Add(new CustomShareLinkRuntime(link.Name, link.IconKind, url, link.IconPath));
        }
    }
}
