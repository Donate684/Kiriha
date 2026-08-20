using Kiriha.Services.Data.Metadata;
using Kiriha.Services.Data.Settings;
using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Kiriha.Mpv.UI.ViewModels.Player;
using Kiriha.Views.Player;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Services.AppLifecycle;

public sealed partial class PlayerModeCoordinator
{
    private PlayerWindow CreatePlayerWindow(string[] args)
    {
        var videoUrl = GetPlayerVideoUrl(args);

        var metadataResolver = _serviceProvider.GetRequiredService<Kiriha.Mpv.UI.Services.Player.IPlayerMediaMetadataResolver>();
        var settingsService = _serviceProvider.GetRequiredService<SettingsService>();
        var playerVm = new PlayerViewModel(videoUrl, metadataResolver.Resolve(videoUrl), metadataResolver, settingsService, _serviceProvider.GetRequiredService<Kiriha.Core.Abstractions.Services.ILocalizer>());
        return new PlayerWindow(settingsService) { DataContext = playerVm };
    }

    private bool TryReplacePlayerWindow(string[] args)
    {
        if (_app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return false;

        var window = desktop.Windows.OfType<PlayerWindow>().LastOrDefault();
        if (window?.DataContext is not PlayerViewModel vm)
            return false;

        var videoUrl = GetPlayerVideoUrl(args);
        if (string.IsNullOrWhiteSpace(videoUrl))
            return false;

        vm.LoadVideo(videoUrl);
        window.Show();
        window.Activate();
        return true;
    }
}

