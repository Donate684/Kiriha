using Kiriha.Core.Domain.Utils.Parsing;
using System.Collections.Concurrent;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Utils.Async;
using Kiriha.ViewModels.Settings;
using Serilog;

namespace Kiriha.ViewModels.NowPlaying;

public partial class NowPlayingViewModel
{
    private readonly ConcurrentDictionary<(AnimeEntity Item, long Generation), byte> _detailsLoading = new();

    /// <summary>
    /// Resolved user-defined share buttons for the currently matched anime.
    /// Refreshed via <see cref="OnMatchedAnimeChanged"/>.
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<CustomShareLinkRuntime> CustomShareLinks { get; } = new();

    private IReadOnlyList<string> _allAlternativeTitles = [];
    public IEnumerable<string> AllAlternativeTitles => _allAlternativeTitles;

    public bool HasAlternativeTitles => AllAlternativeTitles.Any();

    private static bool NeedsDetailsEnrichment(AnimeEntity anime) =>
        anime.IsMissingDetails || (anime.MediaKind == MediaKind.Anime && anime.TotalEpisodes <= 0);

    partial void OnMatchedAnimeChanged(AnimeEntity? value)
    {
        UpdateAlternativeTitles(value);
        UpdateCustomShareLinks(value);

        if (value is null)
        {
            IsLoadingDetails = false;
            return;
        }

        if (NeedsDetailsEnrichment(value))
        {
            EnsureFullDetailsSafeAsync(value).SafeFireAndForget("NowPlaying.OnMatchedAnimeChanged");
        }
        else
        {
            IsLoadingDetails = false;
            EnsureLocalizedSafeAsync(value).SafeFireAndForget("NowPlaying.OnMatchedAnimeChangedLoc");
        }
    }

    public async Task EnsureFullDetailsSafeAsync(AnimeEntity anime)
    {
        if (anime is null || anime.Id <= 0) return;
        if (!NeedsDetailsEnrichment(anime)) return;

        var key = new MediaKey(anime.Id, anime.MediaKind);
        var generation = _animeRepo.GetEntryGeneration(key.Id, key.Kind);
        var request = (anime, generation);
        if (!_detailsLoading.TryAdd(request, 0)) return;

        IsLoadingDetails = true;
        try
        {
            Log.Information("NowPlaying: Anime {Id} ({Title}) is missing details, requesting full data...", key.Id, anime.Title);

            var ct = _disposeCts.Token;
            var full = key.Kind == MediaKind.Anime
                ? await _malApi.GetAnimeDetailsAsync(key.Id, ct)
                : await _malApi.GetMangaDetailsAsync(key.Id, ct);
            if (!IsCurrentMatch(anime, key, generation, ct)) return;

            if (full != null && full.Id == key.Id && (full.MediaKind == MediaKind.Anime) == (key.Kind == MediaKind.Anime))
            {
                var current = _animeRepo.FindById(key.Id, key.Kind);
                if (current != null)
                    await _animeRepo.MergeTrackedDetailsAsync(full, key, generation, ct);
                if (!IsCurrentMatch(anime, key, generation, ct)) return;
                if (!ReferenceEquals(anime, current))
                    anime.MergeFullDetails(full, metadataOnly: true);
            }

            await EnsureLocalizedSafeAsync(anime, key, generation, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warning(ex, "NowPlaying: Failed to fetch full details for anime {Id}", key.Id);
        }
        finally
        {
            _detailsLoading.TryRemove(request, out _);
            IsLoadingDetails = MatchedAnime is { } matched && _detailsLoading.ContainsKey(
                (matched, _animeRepo.GetEntryGeneration(matched.Id, matched.MediaKind)));
        }
    }

    private bool IsCurrentMatch(AnimeEntity anime, MediaKey key, long generation, CancellationToken ct) =>
        !ct.IsCancellationRequested && ReferenceEquals(MatchedAnime, anime) &&
        _animeRepo.GetEntryGeneration(key.Id, key.Kind) == generation;

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
            var url = CustomLinkResolver.Resolve(link.UrlTemplate, value);
            CustomShareLinks.Add(new CustomShareLinkRuntime(link.Name, link.IconKind, url, link.IconPath));
        }
    }
}
