using Avalonia.Controls.ApplicationLifetimes;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Mpv.UI.ViewModels.Player;
using Kiriha.Services.Data.Settings;
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
        var localizer = _serviceProvider.GetRequiredService<ILocalizer>();
        var torrServer = _serviceProvider.GetService<ITorrServerService>();

        var cliMetadata = ExtractMetadataFromArgs(args);
        var initialMetadata = cliMetadata ?? metadataResolver.Resolve(videoUrl);

        var playerVm = new PlayerViewModel(videoUrl, initialMetadata, metadataResolver, settingsService, localizer, torrServer);
        if (cliMetadata != null)
        {
            playerVm.ApplyExternalMetadata(cliMetadata);
        }

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
        var cliMetadata = ExtractMetadataFromArgs(args);
        if (cliMetadata != null)
        {
            vm.ApplyExternalMetadata(cliMetadata);
        }

        window.Show();
        window.Activate();
        return true;
    }
}

