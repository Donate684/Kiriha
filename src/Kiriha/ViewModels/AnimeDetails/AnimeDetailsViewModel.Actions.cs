using Kiriha.ViewModels.Main;
using Kiriha.ViewModels.NowPlaying;
using Kiriha.ViewModels.Dialogs;
using Kiriha.ViewModels.Startup;
using Kiriha.ViewModels.Settings;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Infrastructure.Platform;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.ViewModels.AnimeDetails;

public partial class AnimeDetailsViewModel
{
    [RelayCommand]
    private async Task CopyMalLink()
    {
        string type = Anime.MediaKind == MediaKind.Anime ? "anime" : "manga";
        string url = $"https://myanimelist.net/{type}/{Anime.Id}";
        await CopyToClipboard(url);
    }

    [RelayCommand]
    private async Task CopyShikiLink()
    {
        string baseUrl = ShikiEndpoints.WebsiteUrl(_settingsService.Current.Api.ShikiMirror, Anime.MediaKind);
        string url = $"{baseUrl}{Anime.Id}";
        await CopyToClipboard(url);
    }

    private void OpenInBrowser(string url)
    {
        ShellLauncher.OpenUrl(url);
    }

    [RelayCommand]
    private void OpenMalLink()
    {
        string type = Anime.MediaKind == MediaKind.Anime ? "anime" : "manga";
        OpenInBrowser($"https://myanimelist.net/{type}/{Anime.Id}");
    }

    [RelayCommand]
    private void OpenShikiLink()
    {
        string baseUrl = ShikiEndpoints.WebsiteUrl(_settingsService.Current.Api.ShikiMirror, Anime.MediaKind);
        OpenInBrowser($"{baseUrl}{Anime.Id}");
    }



    private async Task CopyToClipboard(string text)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.Clipboard != null)
        {
            await desktop.MainWindow.Clipboard.SetTextAsync(text);
        }
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
}
