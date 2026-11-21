using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Infrastructure.Tracking.Integration;
using Kiriha.Mpv.UI.ViewModels.Player;

namespace Kiriha.Tests;

public sealed partial class ScrobbleSyncRestartTests
{
    [Fact]
    public async Task PlayerIpcMetadata_RoutesOnlyToTheMatchingOpenRequest()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        string pipeName = $"Kiriha.MetadataRoute.{Guid.NewGuid():N}";
        using var server = new InternalPlayerServer(pipeName);
        await server.StartAsync(ct);
        await using var first = await PlaybackPipe.ConnectAsync(pipeName, ct);
        await using var second = await PlaybackPipe.ConnectAsync(pipeName, ct);
        try
        {
            var original = Metadata("episode-01.mkv", "1", "first");
            var next = Metadata("episode-02.mkv", "2", "second");
            Assert.Null(await first.HandshakeAsync(PlaybackState(original), ct));
            await first.FenceAsync(ct);
            server.StagePlaybackMetadata(next);

            // A newer update for the active request cannot replace the pending launch metadata.
            var firstDelivery = first.ReadMetadataFrameAsync(ct);
            await server.SendMetadataAsync(original, ct);
            Assert.Equal(original, await firstDelivery);
            Assert.Equal(next, await second.HandshakeAsync(PlaybackState(next), ct));
            await second.FenceAsync(ct);
            Assert.Empty(await second.ReadMetadataAsync(ct));

            var unrelated = next with { PlaybackRequestId = original.PlaybackRequestId };
            await server.SendMetadataAsync(unrelated, ct);
            await server.SendMetadataAsync(next with { PlaybackSourceKey = "different-source" }, ct);
            var update = next with { TitleRomaji = "Corrected title" };
            var secondDelivery = second.ReadMetadataFrameAsync(ct);
            await server.SendMetadataAsync(update, ct);
            Assert.Equal(update, await secondDelivery);
            Assert.Empty(await second.ReadMetadataAsync(ct));
            Assert.Empty(await first.ReadMetadataAsync(ct));

            // The resident player opens a different file without reconnecting.
            var third = Metadata("episode-03.mkv", "3", "third");
            server.StagePlaybackMetadata(third);
            var newState = PlaybackState(third);
            newState.Episode = string.Empty;
            newState.OriginalTitle = "master.m3u8";
            var thirdDelivery = second.ReadMetadataFrameAsync(ct);
            await second.SendStateAsync(newState, ct);
            Assert.Equal(third, await thirdDelivery);
            Assert.Equal((third.OriginalTitle, third.EpisodeText),
                (server.LatestState?.OriginalTitle, server.LatestState?.Episode));
            await server.SendMetadataAsync(update, ct);
            Assert.Empty(await second.ReadMetadataAsync(ct));

            await second.CloseAsync(ct);
            await second.FenceAsync(ct);
            await using var reconnected = await PlaybackPipe.ConnectAsync(pipeName, ct);
            Assert.Equal(third, await reconnected.HandshakeAsync(PlaybackState(third), ct));
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task PlayerIpcMetadata_LegacyHandshakeRequiresMatchingFileAndEpisode()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        string pipeName = $"Kiriha.LegacyMetadata.{Guid.NewGuid():N}";
        using var server = new InternalPlayerServer(pipeName);
        await server.StartAsync(ct);
        await using var first = await PlaybackPipe.ConnectAsync(pipeName, ct);
        await using var second = await PlaybackPipe.ConnectAsync(pipeName, ct);
        await using var third = await PlaybackPipe.ConnectAsync(pipeName, ct);
        try
        {
            var legacy = Metadata("episode-01.mkv", "1", null);
            await first.HandshakeAsync(PlaybackState(legacy), ct);
            await first.FenceAsync(ct);
            var delivery = first.ReadMetadataFrameAsync(ct);
            await server.SendMetadataAsync(legacy, ct);
            Assert.Equal(legacy, await delivery);

            var sameTorrentDifferentEpisode = PlaybackState(legacy);
            sameTorrentDifferentEpisode.OriginalTitle = "episode-02.mkv";
            sameTorrentDifferentEpisode.Episode = "2";
            Assert.Null(await second.HandshakeAsync(sameTorrentDifferentEpisode, ct));
            Assert.Empty(await second.ReadMetadataAsync(ct));
            Assert.Null(await third.HandshakeAsync(null, ct));
            Assert.Empty(await third.ReadMetadataAsync(ct));
            Assert.Equal(legacy, await third.HandshakeAsync(PlaybackState(legacy), ct));
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task PlayerIpcMetadata_WhenPreviousConnectionStopsReading_NewOwnerStillReceivesUpdates()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        string pipeName = $"Kiriha.BlockedMetadata.{Guid.NewGuid():N}";
        using var server = new InternalPlayerServer(pipeName);
        await server.StartAsync(ct);
        await using var previous = await PlaybackPipe.ConnectAsync(pipeName, ct);
        await using var current = await PlaybackPipe.ConnectAsync(pipeName, ct);
        try
        {
            var first = Metadata("episode-01.mkv", "1", "first");
            var second = Metadata("episode-02.mkv", "2", "second");
            await previous.HandshakeAsync(PlaybackState(first), ct);
            await previous.FenceAsync(ct);
            var stalled = server.SendMetadataAsync(first with { TitleRomaji = new string('x', 128 * 1024) });

            Assert.Null(await current.HandshakeAsync(PlaybackState(second), ct));
            await current.FenceAsync(ct);
            var delivery = current.ReadMetadataFrameAsync(ct);
            await server.SendMetadataAsync(second, ct);
            Assert.Equal(second, await delivery);
            await stalled.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.Equal(second.PlaybackRequestId, server.LatestState?.PlaybackRequestId);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlayerIpcMetadata_WhenUiDispatchIsDelayed_DropsMetadataQueuedBeforeReload(bool tagged)
    {
        const string file = "episode-01.mkv";
        var original = Metadata(file, "1", tagged ? "first" : null) with { PlaybackSourceKey = null };
        using var player = new PlayerViewModel(file, original, null, null, ILocalizer.Passthrough, requestId: "first");
        var actions = new Queue<Action>();
        var late = original with { TitleRomaji = "Late title" };
        player.QueueIpcMetadata(late, actions.Enqueue);
        player.LoadVideo(file, metadata: original with { TitleRomaji = "Reopened title" }, requestId: "second");

        actions.Dequeue().Invoke();
        Assert.Equal("Reopened title", player.AnimeTitleRomaji);
        if (tagged)
        {
            // Delivery can also arrive after the new open, when the UI callback alone can't detect the change.
            player.QueueIpcMetadata(late, actions.Enqueue);
            actions.Dequeue().Invoke();
            Assert.Equal("Reopened title", player.AnimeTitleRomaji);
        }

        var current = original with { PlaybackRequestId = "second", TitleRomaji = "Current title" };
        player.QueueIpcMetadata(current, actions.Enqueue);
        actions.Dequeue().Invoke();
        Assert.Equal("Current title", player.AnimeTitleRomaji);
    }

    [Fact]
    public void PlayerIpcMetadata_SameAnimeAndTorrentHash_CannotOverrideDifferentEpisode()
    {
        var original = Metadata("episode-02.mkv", "2", "second") with { PlaybackSourceKey = null };
        using var player = new PlayerViewModel(original.OriginalTitle, original, null, null, ILocalizer.Passthrough, requestId: "second");
        var old = original with { PlaybackRequestId = null, OriginalTitle = "episode-01.mkv", EpisodeText = "1" };
        Assert.False(player.MatchesIpcMetadata(old));
        Assert.False(player.MatchesIpcMetadata(old with { OriginalTitle = original.OriginalTitle }));
        Assert.True(player.MatchesIpcMetadata(original with { PlaybackRequestId = null, EpisodeText = "02" }));
    }

    private static PlayerMediaMetadata Metadata(string file, string episode, string? requestId) =>
        new(file, "Russian title", "English title", episode, AnimeId, "Romaji title", "same-torrent")
        {
            PlaybackRequestId = requestId,
            PlaybackSourceKey = requestId == null ? null : ParsedMedia.CreatePlaybackSourceKey(file)
        };

    private static InternalPlayerState PlaybackState(PlayerMediaMetadata metadata) => new()
    {
        OriginalTitle = metadata.OriginalTitle,
        AnimeId = metadata.AnimeId,
        Episode = metadata.EpisodeText,
        PlaybackRequestId = metadata.PlaybackRequestId,
        PlaybackSourceKey = metadata.PlaybackSourceKey,
        TorrentHash = metadata.TorrentHash,
        IsPlaying = true
    };
}
