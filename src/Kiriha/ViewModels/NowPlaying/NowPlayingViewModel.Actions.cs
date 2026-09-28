using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Models;
using Serilog;

namespace Kiriha.ViewModels.NowPlaying;

public partial class NowPlayingViewModel
{
    [RelayCommand]
    private async Task AddToWatching()
    {
        if (MatchedAnime is null) return;

        try
        {
            if (await _progressService.UpdateProgressAsync(MatchedAnime, MatchedAnime.Progress, UserAnimeStatus.Watching))
            {
                await _animeRepo.AddOrUpdateAnimeAsync(MatchedAnime);
                WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
            }

            OnPropertyChanged(nameof(IsNotInList));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to add anime to watching");
        }
    }

    [RelayCommand]
    private void GoToSettings()
    {
        WeakReferenceMessenger.Default.Send(new NavigationMessage(NavigationPage.Settings));
    }
}
