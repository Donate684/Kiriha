using System.Text.Json;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Infrastructure.Player;

namespace Kiriha.Infrastructure.Tracking.Integration;

public partial class InternalPlayerServer
{
    private const int MaxPendingPlaybackMetadata = 128;

    public void StagePlaybackMetadata(PlayerMediaMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.PlaybackRequestId)) return;

        lock (_stateGate)
        {
            var requestId = metadata.PlaybackRequestId;
            if (_pendingPlaybackMetadata.Count >= MaxPendingPlaybackMetadata && !_pendingPlaybackMetadata.ContainsKey(requestId))
                _pendingPlaybackMetadata.Remove(_pendingPlaybackMetadata.Keys.First());
            _pendingPlaybackMetadata[requestId] = metadata;
        }
    }

    public async Task SendMetadataAsync(PlayerMediaMetadata metadata, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task? write = null;
        bool forward = false;
        lock (_stateGate)
        {
            if (_latestState is { } state)
            {
                bool unboundLegacySession = !metadata.HasPlaybackIdentity && !HasPlaybackIdentity(state);
                if (!unboundLegacySession && !MatchesState(metadata, state)) return;
            }
            else if (metadata.HasPlaybackIdentity)
            {
                return;
            }

            _latestMetadata = metadata;
            if (_activeSession is { } target)
            {
                write = SendMetadataToSessionAsync(target, metadata, cancellationToken);
            }
            else
            {
                forward = !HasConnectedClients;
                _sendLegacyMetadataOnAccept = !forward;
            }
        }

        if (write != null)
        {
            await write.ConfigureAwait(false);
        }
        else if (forward)
        {
            PlayerProcessBridge.ForwardMetadata(metadata.OriginalTitle, metadata.AnimeId ?? 0,
                metadata.TitleRu, metadata.TitleEn, metadata.TitleRomaji, metadata.EpisodeText, metadata.TorrentHash);
        }
    }

    private PlayerMediaMetadata? FindMetadata(InternalPlayerState? state)
    {
        if (state == null || state.IsClosed) return null;

        lock (_stateGate)
        {
            if (FindStagedMetadata(state) is { } pending)
                return pending;
            return _latestMetadata is { } latest && MatchesState(latest, state) ? latest : null;
        }
    }

    private PlayerMediaMetadata? FindStagedMetadata(InternalPlayerState state) =>
        state.PlaybackRequestId is { } id && _pendingPlaybackMetadata.TryGetValue(id, out var pending) &&
        MatchesState(pending, state) ? pending : null;

    private static bool MatchesState(PlayerMediaMetadata metadata, InternalPlayerState state) =>
        metadata.MatchesPlayback(state.PlaybackRequestId, state.PlaybackSourceKey, state.OriginalTitle, state.Episode);

    private static void EnrichState(InternalPlayerState state, PlayerMediaMetadata metadata)
    {
        state.AnimeId ??= metadata.AnimeId;
        if (string.IsNullOrEmpty(state.OriginalTitle) || PlayerMediaMetadata.IsStreamManifest(state.OriginalTitle))
            state.OriginalTitle = metadata.OriginalTitle;
        if (string.IsNullOrEmpty(state.AnimeTitle) || PlayerMediaMetadata.IsStreamManifest(state.AnimeTitle))
            state.AnimeTitle = !string.IsNullOrEmpty(metadata.TitleRu) ? metadata.TitleRu
                : !string.IsNullOrEmpty(metadata.TitleEn) ? metadata.TitleEn : metadata.OriginalTitle;
        if (string.IsNullOrEmpty(state.Episode))
            state.Episode = metadata.EpisodeText;
    }

    private static Task<bool> SendMetadataToSessionAsync(ClientSession session, PlayerMediaMetadata metadata, CancellationToken ct)
    {
        var message = new InternalPlayerIpcMessage { Type = InternalPlayerIpcMessage.TypeMetadata, Metadata = metadata };
        return SendToSessionAsync(session,
            JsonSerializer.Serialize(message, InternalPlayerStateJsonContext.Default.InternalPlayerIpcMessage), ct);
    }
}
