using Avalonia.Controls.ApplicationLifetimes;
using Kiriha.Core.Domain.Models;
using Kiriha.Infrastructure.Player;
using Kiriha.Mpv.UI.ViewModels.Player;
using Kiriha.Infrastructure.Settings;
using Kiriha.Views.Player;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Services.AppLifecycle;

public sealed partial class PlayerModeCoordinator
{
#pragma warning disable S3168 // Action<string[]> callback delegate for IPC command server
    private async void HandlePlayerCommand(string[] args)
#pragma warning restore S3168
    {
        if (args.Any(arg => arg.Equals(PlayerProcessBridge.ShutdownArg, StringComparison.OrdinalIgnoreCase)))
        {
            if (_app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (var playerWindow in desktop.Windows.OfType<PlayerWindow>().ToArray())
                    playerWindow.Close();

                var commandServer = _playerCommandServer;
                _playerCommandServer = null;
                if (commandServer != null)
                    await commandServer.DisposeAsync();

                if (_shutdownCoordinator != null)
                {
                    await _shutdownCoordinator.DrainAsync();
                }
                else
                {
                    FlushSettings();
                }

                if (_serviceProvider is IDisposable disposable)
                    disposable.Dispose();
                desktop.Shutdown();
            }

            return;
        }

        if (args.Any(arg => arg.Equals(PlayerProcessBridge.UpdateMetadataArg, StringComparison.OrdinalIgnoreCase)))
        {
            ApplyPlayerMetadataCommand(args);
            return;
        }

        if (!IsPlayerMode(args) || PlayerProcessBridge.IsResident(args))
            return;

        var settingsService = _serviceProvider.GetRequiredService<SettingsService>();
        if (string.IsNullOrWhiteSpace(GetPlayerVideoUrl(args)) &&
            _app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime existingDesktop &&
            existingDesktop.Windows.OfType<PlayerWindow>().LastOrDefault() is { } existingWindow)
        {
            existingWindow.Show();
            existingWindow.Activate();
            return;
        }

        if (settingsService.Current.Player.SingleWindow && TryReplacePlayerWindow(args))
            return;

        var window = CreatePlayerWindow(args);
        window.Show();
        window.Activate();
    }

    private void ApplyPlayerMetadataCommand(string[] args)
    {
        var originalTitle = GetArgValue(args, "--original-title") ?? string.Empty;
        var titleRu = GetArgValue(args, "--title-ru") ?? string.Empty;
        var titleEn = GetArgValue(args, "--title-en") ?? string.Empty;
        var titleRomaji = GetArgValue(args, "--title-romaji") ?? string.Empty;
        var episodeText = GetArgValue(args, "--episode") ?? string.Empty;
        var torrentHash = GetArgValue(args, "--torrent-hash") ?? string.Empty;
        int? animeId = int.TryParse(GetArgValue(args, "--anime-id"), out var parsedAnimeId) && parsedAnimeId > 0
            ? parsedAnimeId
            : null;

        if (_app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var metadata = new PlayerMediaMetadata(originalTitle, titleRu, titleEn, episodeText, animeId, titleRomaji, torrentHash);
        var matching = desktop.Windows.OfType<PlayerWindow>()
            .Select(window => window.DataContext).OfType<PlayerViewModel>()
            .Where(vm => vm.MatchesIpcMetadata(metadata)).Take(2).ToArray();
        if (matching.Length == 1)
            matching[0].ApplyExternalMetadata(metadata);
    }
}
