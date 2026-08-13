using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.ViewModels.NowPlaying;

public partial class NowPlayingViewModel
{
    [RelayCommand]
    private async Task ManualMatch()
    {
        if (CurrentMedia == null) return;

        SearchQuery = CurrentMedia.AnimeTitle;
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
        }
        else
        {
            await _trackingService.AddNegativeMappingAsync();
            MatchedAnime = null;
            IsManuallyMapped = false;
            if (CurrentMedia != null) SearchQuery = CurrentMedia.AnimeTitle;
            await OpenSearchPanel();
        }
    }

    [RelayCommand]
    private void ConfirmMatch()
    {
        if (PendingMatch == null) return;
        MatchedAnime = PendingMatch;
        PendingMatch = null;
    }

    [RelayCommand]
    private void RejectMatch()
    {
        PendingMatch = null;
    }
}
