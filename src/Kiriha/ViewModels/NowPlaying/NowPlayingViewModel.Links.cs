using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Infrastructure.Platform;

namespace Kiriha.ViewModels.NowPlaying;

public partial class NowPlayingViewModel
{
    [RelayCommand]
    private void OpenMalLink()
    {
        if (MatchedAnime is null) return;
        ShellLauncher.OpenUrl($"{Kiriha.Core.Domain.Constants.AppConstants.Api.Mal.WebsiteUrl}{MatchedAnime.Id}");
    }

    [RelayCommand]
    private void OpenShikiLink()
    {
        if (MatchedAnime is null) return;
        ShellLauncher.OpenUrl($"{ShikiEndpoints.WebsiteUrl(_settingsService.Current.Api.ShikiMirror)}{MatchedAnime.Id}");
    }
}
