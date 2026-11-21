using Avalonia.Controls.ApplicationLifetimes;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Mpv.UI.ViewModels.Player;
using Kiriha.Infrastructure.Settings;
using Kiriha.Views.Player;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Services.AppLifecycle;

public sealed partial class PlayerModeCoordinator
{
    private PlayerWindow CreatePlayerWindow(string[] args)
    {
        var player = CreatePlayerViewModel(args, _serviceProvider);
        var window = new PlayerWindow(_serviceProvider.GetRequiredService<SettingsService>()) { DataContext = player };
        _playerWindows.Track(window);
        window.Closed += OnPlayerWindowClosed;
        return window;
    }

    internal static PlayerViewModel CreatePlayerViewModel(string[] args, IServiceProvider serviceProvider)
    {
        var videoUrl = GetPlayerVideoUrl(args);
        var subtitleUrl = GetArgValue(args, "--sub-file") ?? GetArgValue(args, "--subtitle");

        var metadataResolver = serviceProvider.GetRequiredService<Kiriha.Mpv.UI.Services.Player.IPlayerMediaMetadataResolver>();
        var settingsService = serviceProvider.GetRequiredService<SettingsService>();
        var localizer = serviceProvider.GetRequiredService<ILocalizer>();
        var torrServer = serviceProvider.GetService<ITorrServerService>();

        var cliMetadata = ExtractMetadataFromArgs(args);
        var initialMetadata = cliMetadata ?? metadataResolver.Resolve(videoUrl);
        var headers = ExtractHeadersFromArgs(args);
        var resumeStore = serviceProvider.GetService<IPlaybackResumeStore>();

        return new PlayerViewModel(videoUrl, initialMetadata, metadataResolver, settingsService, localizer,
            torrServer, subtitleUrl, headers, resumeStore, GetPlaybackRequestId(args));
    }

    private PlayerWindow? TryReplacePlayerWindow(string[] args)
    {
        if (_app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        var window = desktop.Windows.OfType<PlayerWindow>().LastOrDefault();
        if (window?.DataContext is not PlayerViewModel vm)
            return null;

        var videoUrl = GetPlayerVideoUrl(args);
        if (string.IsNullOrWhiteSpace(videoUrl))
        {
            window.Show();
            window.Activate();
            return window;
        }

        var subtitleUrl = GetArgValue(args, "--sub-file") ?? GetArgValue(args, "--subtitle");
        var cliMetadata = ExtractMetadataFromArgs(args);
        var headers = ExtractHeadersFromArgs(args);
        vm.LoadVideo(videoUrl, subtitleUrl, cliMetadata, headers, GetPlaybackRequestId(args));

        window.Show();
        window.Activate();
        return window;
    }
}

