using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Infrastructure.Platform;
using Kiriha.ViewModels.Dialogs;

namespace Kiriha.ViewModels.AnimeDetails;

public partial class AnimeDetailsViewModel
{
    private void OpenInBrowser(string url)
    {
        ShellLauncher.OpenUrl(url);
    }

    [RelayCommand]
    private void OpenMalLink()
    {
        string type = Anime.MediaKind == MediaKind.Anime ? "anime" : "manga";
        OpenInBrowser($"{Kiriha.Core.Domain.Constants.AppConstants.Api.Mal.BaseWebsiteUrl}{type}/{Anime.Id}");
    }

    [RelayCommand]
    private void OpenShikiLink()
    {
        string baseUrl = ShikiEndpoints.WebsiteUrl(_settingsService.Current.Api.ShikiMirror, Anime.MediaKind);
        OpenInBrowser($"{baseUrl}{Anime.Id}");
    }

    [RelayCommand]
    private void ShowFranchiseGraph()
    {
        var vm = new FranchiseGraphViewModel(Anime.Id, _shikiApiService, _malApiService, _dialogs, _animeRepo);
        var window = new Kiriha.Views.FranchiseGraphWindow
        {
            DataContext = vm
        };

        // Show as a non-modal window or modal, depending on preference. Non-modal is better so user can keep it open.
        window.Show();
    }

    [RelayCommand]
    private void OpenPosterPreview()
    {
        if (!string.IsNullOrEmpty(Anime?.MainPictureUrl))
        {
            IsPosterPreviewOpen = true;
        }
    }

    [RelayCommand]
    private void ClosePosterPreview()
    {
        IsPosterPreviewOpen = false;
    }
}
