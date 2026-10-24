using CommunityToolkit.Mvvm.Input;

namespace Kiriha.ViewModels.NowPlaying;

public partial class NowPlayingViewModel
{
    private static string ResolveBestSearchQuery(Kiriha.Core.Domain.Models.ParsedMedia? media)
    {
        if (media is null) return string.Empty;
        if (!string.IsNullOrWhiteSpace(media.OriginalTitle))
        {
            var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(media.OriginalTitle);
            if (!string.IsNullOrWhiteSpace(parsed.Title))
                return parsed.Title;
        }
        return media.AnimeTitle;
    }

    [RelayCommand]
    private async Task ManualMatch()
    {
        if (CurrentMedia is null) return;

        SearchQuery = ResolveBestSearchQuery(CurrentMedia);
        await SearchSuggestions();
    }

    [RelayCommand]
    private async Task RemoveMapping()
    {
        await _trackingService.RemoveManualMappingAsync();
    }

    [RelayCommand]
    private async Task UnlinkMatch()
    {
        if (IsManuallyMapped)
        {
            await _trackingService.RemoveManualMappingAsync();
            if (MatchedAnime is null)
            {
                if (CurrentMedia != null) SearchQuery = ResolveBestSearchQuery(CurrentMedia);
                await OpenSearchPanel();
            }
        }
        else
        {
            await _trackingService.AddNegativeMappingAsync();
            MatchedAnime = null;
            IsManuallyMapped = false;
            if (CurrentMedia != null) SearchQuery = ResolveBestSearchQuery(CurrentMedia);
            await OpenSearchPanel();
        }
    }

    [RelayCommand]
    private void ConfirmMatch()
    {
        if (PendingMatch is null) return;
        MatchedAnime = PendingMatch;
        PendingMatch = null;
    }

    [RelayCommand]
    private void RejectMatch()
    {
        PendingMatch = null;
    }
}
