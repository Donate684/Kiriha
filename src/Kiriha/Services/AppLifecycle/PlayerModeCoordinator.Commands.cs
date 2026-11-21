using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Kiriha.Core.Domain.Models;
using Kiriha.Infrastructure.Player;
using Kiriha.Mpv.UI.ViewModels.Player;
using Kiriha.Infrastructure.Settings;
using Kiriha.Views.Player;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Services.AppLifecycle;

public sealed partial class PlayerModeCoordinator
{
    private readonly PlayerPlaybackRequests _playbackRequests = new();

    private void HandlePlayerCommand(string[] args)
    {
        if (_shutdown?.IsPreparing == true)
            return;

        if (args.Any(arg => arg.Equals(PlayerProcessBridge.UpdateMetadataArg, StringComparison.OrdinalIgnoreCase)))
        {
            ApplyPlayerMetadataCommand(args);
            return;
        }

        if (!IsPlayerMode(args) || PlayerProcessBridge.IsResident(args))
            return;

        var requestId = GetPlaybackRequestId(args);
        if (requestId != null && PlayerPlaybackProtocol.IsValidRequestId(requestId))
        {
            AcceptPlayback(args, requestId);
            return;
        }

        OpenPlayerWindow(args);
    }

    private async Task<PlayerLaunchStatus> ReceivePlaybackAsync(string[] frame, CancellationToken ct) =>
        await Dispatcher.UIThread.InvokeAsync(() =>
            _playbackRequests.Handle(frame, _shutdown?.IsPreparing == true, OpenRequest), DispatcherPriority.Normal, ct);

    private void AcceptPlayback(string[] args, string requestId) =>
        _playbackRequests.Open(requestId, PlayerPlaybackRequests.IsExpired(args), () => OpenRequest(args, requestId));

    private void OpenInitialWindow(IClassicDesktopStyleApplicationLifetime desktop, string[] args)
    {
        var requestId = GetPlaybackRequestId(args);
        if (requestId == null)
        {
            desktop.MainWindow = CreatePlayerWindow(args);
            return;
        }

        if (!PlayerPlaybackProtocol.IsValidRequestId(requestId) ||
            _playbackRequests.Open(requestId, PlayerPlaybackRequests.IsExpired(args), () =>
            {
                var window = CreatePlayerWindow(args);
                desktop.MainWindow = window;
                return ObserveRequest(window, requestId, GetPlayerVideoUrl(args));
            }) == PlayerLaunchStatus.Failed)
        {
            _ = CompleteLocalShutdownAsync();
        }
    }

    private PlayerPlaybackRequests.Target OpenRequest(string[] args, string requestId) =>
        ObserveRequest(OpenPlayerWindow(args), requestId, GetPlayerVideoUrl(args));

    private static PlayerPlaybackRequests.Target ObserveRequest(PlayerWindow window, string id, string media)
    {
        var reference = new WeakReference<PlayerWindow>(window);
        PlayerLaunchStatus Read()
        {
            if (!reference.TryGetTarget(out var target) || target.DataContext is not PlayerViewModel player)
                return PlayerLaunchStatus.Superseded;
            if (string.IsNullOrWhiteSpace(media))
                return PlayerLaunchStatus.Ready;
            return player.GetOpeningState(id) switch
            {
                PlaybackOpeningState.Pending => PlayerLaunchStatus.Accepted,
                PlaybackOpeningState.Ready => PlayerLaunchStatus.Ready,
                PlaybackOpeningState.Failed => PlayerLaunchStatus.Failed,
                _ => PlayerLaunchStatus.Superseded
            };
        }

        void Cancel()
        {
            if (reference.TryGetTarget(out var target) && Read() == PlayerLaunchStatus.Accepted)
                target.Close();
        }

        return new PlayerPlaybackRequests.Target(Read, Cancel);
    }

    private PlayerWindow OpenPlayerWindow(string[] args)
    {
        var settingsService = _serviceProvider.GetRequiredService<SettingsService>();
        if (string.IsNullOrWhiteSpace(GetPlayerVideoUrl(args)) &&
            _app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime existingDesktop &&
            existingDesktop.Windows.OfType<PlayerWindow>().LastOrDefault() is { } existingWindow)
        {
            existingWindow.Show();
            existingWindow.Activate();
            return existingWindow;
        }

        if (settingsService.Current.Player.SingleWindow && TryReplacePlayerWindow(args) is { } replaced)
            return replaced;

        var window = CreatePlayerWindow(args);
        try
        {
            window.Show();
            if (_initialCommandDeadline != null && _app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = window;
                _initialCommandDeadline.Dispose();
                _initialCommandDeadline = null;
            }
            window.Activate();
            return window;
        }
        catch
        {
            window.Close();
            throw;
        }
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
