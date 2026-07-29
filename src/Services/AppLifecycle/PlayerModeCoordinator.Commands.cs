using Kiriha.Services.Data.Settings;
using Kiriha.Core.Player;
using System;
using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Kiriha.Models;
using Kiriha.ViewModels.Player;
using Kiriha.Views.Player;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Services.AppLifecycle;

public sealed partial class PlayerModeCoordinator
{
    private void HandlePlayerCommand(string[] args)
    {
        if (args.Any(arg => arg.Equals(PlayerProcessBridge.ShutdownArg, StringComparison.OrdinalIgnoreCase)))
        {
            if (_app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (var playerWindow in desktop.Windows.OfType<PlayerWindow>().ToArray())
                    playerWindow.Close();

                FlushSettings();
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
        var episodeText = GetArgValue(args, "--episode") ?? string.Empty;
        int? animeId = int.TryParse(GetArgValue(args, "--anime-id"), out var parsedAnimeId) ? parsedAnimeId : null;

        if (_app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var metadata = new PlayerMediaMetadata(originalTitle, titleRu, titleEn, episodeText, animeId);
        var playerWindows = desktop.Windows.OfType<PlayerWindow>().ToArray();
        var updated = false;

        foreach (var playerWindow in playerWindows)
        {
            if (playerWindow.DataContext is not PlayerViewModel vm)
                continue;

            if (!vm.MatchesOriginalTitle(originalTitle))
                continue;

            vm.ApplyExternalMetadata(metadata);
            updated = true;
        }

        if (!updated && playerWindows.LastOrDefault()?.DataContext is PlayerViewModel fallbackVm)
            fallbackVm.ApplyExternalMetadata(metadata);
    }
}
