using System.Text.Json;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Serilog;

namespace Kiriha.Infrastructure.Tracking.Integration;

public partial class InternalPlayerServer
{
    private async Task ProcessIncomingMessageAsync(string line, ClientSession session, CancellationToken ct)
    {
        InternalPlayerIpcMessage? message = null;
        try
        {
            message = JsonSerializer.Deserialize(line, InternalPlayerStateJsonContext.Default.InternalPlayerIpcMessage);
        }
        catch (JsonException)
        {
            // Not a typed message, fallback to state
        }

        if (message != null && !string.IsNullOrEmpty(message.Type))
        {
            switch (message.Type)
            {
                case InternalPlayerIpcMessage.TypeHandshake:
                    var initialMeta = FindMetadata(message.State);
                    if (initialMeta != null && message.State != null)
                        EnrichState(message.State, initialMeta);

                    var ack = new InternalPlayerIpcMessage
                    {
                        Type = InternalPlayerIpcMessage.TypeHandshakeAck,
                        Version = "1.0",
                        Metadata = initialMeta
                    };
                    var ackJson = JsonSerializer.Serialize(ack, InternalPlayerStateJsonContext.Default.InternalPlayerIpcMessage);
                    if (await SendToSessionAsync(session, ackJson, ct).ConfigureAwait(false))
                        await AcceptStateAsync(session, message.State, initialMeta, ct).ConfigureAwait(false);
                    break;

                case InternalPlayerIpcMessage.TypeState:
                    if (message.State != null)
                        await AcceptStateAsync(session, message.State, null, ct).ConfigureAwait(false);
                    break;

                case InternalPlayerIpcMessage.TypeClosed:
                    CloseSession(session);
                    break;

                case InternalPlayerIpcMessage.TypePing:
                    var pong = new InternalPlayerIpcMessage { Type = InternalPlayerIpcMessage.TypePong };
                    await SendToSessionAsync(session, JsonSerializer.Serialize(pong, InternalPlayerStateJsonContext.Default.InternalPlayerIpcMessage), ct).ConfigureAwait(false);
                    break;
            }
            return;
        }

        // Backward compatibility: raw InternalPlayerState JSON
        try
        {
            var state = JsonSerializer.Deserialize(line, InternalPlayerStateJsonContext.Default.InternalPlayerState);
            if (state != null)
                await AcceptStateAsync(session, state, null, ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "InternalPlayerServer: Failed to parse IPC message");
        }
    }

    private async Task AcceptStateAsync(ClientSession session, InternalPlayerState? state,
        PlayerMediaMetadata? acknowledgedMetadata, CancellationToken ct)
    {
        if (state?.IsClosed == true)
        {
            CloseSession(session);
            return;
        }

        // Keep transitions and their notifications in order across connection readers.
        // Subscribers may read LatestState or respond via IPC, so release _stateGate first.
        Task? stagedWrite = null;
        lock (_publicationGate)
        {
            lock (_stateGate)
            {
                if (session.IsClosed || session.ConnectionOrder < _latestOwnerOrder ||
                    (_activeSession != null && (state == null ||
                        (!ReferenceEquals(_activeSession, session) && !HasPlaybackIdentity(state)))))
                    return;

                _activeSession = session;
                _latestOwnerOrder = session.ConnectionOrder;
                state ??= new InternalPlayerState { IsLoading = true };
                if (state.PlaybackRequestId is { } requestId && FindStagedMetadata(state) is { } pending)
                {
                    _pendingPlaybackMetadata.Remove(requestId);
                    _latestMetadata = pending;
                    EnrichState(state, pending);
                    // Queue the launch metadata before subscribers can send a newer update for this state.
                    if (!ReferenceEquals(acknowledgedMetadata, pending))
                        stagedWrite = SendMetadataToSessionAsync(session, pending, ct);
                }
                else if (_sendLegacyMetadataOnAccept && _latestMetadata is { } latest &&
                    !ReferenceEquals(acknowledgedMetadata, latest) &&
                    (!HasPlaybackIdentity(state) || MatchesState(latest, state)))
                {
                    stagedWrite = SendMetadataToSessionAsync(session, latest, ct);
                }

                _sendLegacyMetadataOnAccept = false;
                _latestState = state;
            }

            PublishState(state);
        }

        if (stagedWrite != null)
            await stagedWrite.ConfigureAwait(false);
    }

    private static bool HasPlaybackIdentity(InternalPlayerState state) =>
        state.AnimeId is > 0 || !string.IsNullOrWhiteSpace(state.OriginalTitle) ||
        !string.IsNullOrWhiteSpace(state.AnimeTitle) || !string.IsNullOrWhiteSpace(state.PlaybackRequestId) ||
        !string.IsNullOrWhiteSpace(state.PlaybackSourceKey) || !string.IsNullOrWhiteSpace(state.TorrentHash);

    private void CloseSession(ClientSession session)
    {
        lock (_publicationGate)
        {
            lock (_stateGate)
            {
                session.IsClosed = true;
                if (!ReferenceEquals(_activeSession, session)) return;

                _activeSession = null;
                _latestState = null;
                // Retain the ownership order after close so older connections cannot take over again.
            }

            PublishState(new InternalPlayerState { IsClosed = true });
        }
    }

    private void PublishState(InternalPlayerState state)
    {
        try
        {
            PlayerStateChanged?.Invoke(this, state);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "InternalPlayerServer: A player state subscriber failed");
        }
    }

}
