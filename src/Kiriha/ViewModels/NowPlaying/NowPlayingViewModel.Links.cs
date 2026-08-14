using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Shared.Shiki;
using Kiriha.Infrastructure.Platform;
using Avalonia.Input.Platform;

namespace Kiriha.ViewModels.NowPlaying;

public partial class NowPlayingViewModel
{
    [RelayCommand]
    private async Task CopyMalLink()
    {
        if (MatchedAnime == null) return;
        string url = $"{Kiriha.Core.Domain.Constants.AppConstants.Api.Mal.WebsiteUrl}{MatchedAnime.Id}";
        await CopyToClipboard(url);
    }

    [RelayCommand]
    private async Task CopyShikiLink()
    {
        if (MatchedAnime == null) return;
        string url = $"{ShikiEndpoints.WebsiteUrl(_settingsService.Current.Api.ShikiMirror)}{MatchedAnime.Id}";
        await CopyToClipboard(url);
    }

    [RelayCommand]
    private void OpenMalLink()
    {
        if (MatchedAnime == null) return;
        ShellLauncher.OpenUrl($"{Kiriha.Core.Domain.Constants.AppConstants.Api.Mal.WebsiteUrl}{MatchedAnime.Id}");
    }

    [RelayCommand]
    private void OpenShikiLink()
    {
        if (MatchedAnime == null) return;
        ShellLauncher.OpenUrl($"{ShikiEndpoints.WebsiteUrl(_settingsService.Current.Api.ShikiMirror)}{MatchedAnime.Id}");
    }

    private static async Task CopyToClipboard(string text)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.Clipboard != null)
        {
            await desktop.MainWindow.Clipboard.SetTextAsync(text);
        }
    }
}
