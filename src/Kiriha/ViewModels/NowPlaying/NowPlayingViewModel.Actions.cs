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
            var result = await _listActionService.AddToListAsync(MatchedAnime, UserAnimeStatus.Watching, MatchedAnime.Progress);
            if (!result.Success && !string.IsNullOrEmpty(result.Message))
            {
                Log.Warning("NowPlayingViewModel: Failed to add anime to watching: {Message}", result.Message);
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
