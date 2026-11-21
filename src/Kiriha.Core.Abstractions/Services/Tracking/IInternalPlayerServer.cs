using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;

namespace Kiriha.Core.Abstractions.Services;

public interface IInternalPlayerServer
{
    bool HasConnectedClients => false;
    InternalPlayerState? LatestState { get; }
    event EventHandler<InternalPlayerState>? PlayerStateChanged;
    void StagePlaybackMetadata(PlayerMediaMetadata metadata);
    Task SendMetadataAsync(PlayerMediaMetadata metadata, CancellationToken cancellationToken = default);
}
