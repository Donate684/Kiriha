using Kiriha.ViewModels.Main;
using Kiriha.ViewModels.NowPlaying;
using Kiriha.ViewModels.Dialogs;
using Kiriha.ViewModels.Startup;
using Kiriha.ViewModels.Settings;
using Kiriha.Core.Domain.Constants;
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Infrastructure;
using Kiriha.Infrastructure.Platform;
using Kiriha.Core.Shared.Shiki;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Shared.Messages;
using Serilog;

namespace Kiriha.ViewModels.NowPlaying;

public partial class NowPlayingViewModel
{
    [RelayCommand]
    private async Task AddToWatching()
    {
        if (MatchedAnime == null) return;

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
