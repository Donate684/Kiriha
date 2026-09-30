using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;

namespace Kiriha.Core.Abstractions.Services;

public interface IInternalPlayerServer
{
    event EventHandler<InternalPlayerState>? PlayerStateChanged;
    Task SendMetadataAsync(PlayerMediaMetadata metadata, CancellationToken cancellationToken = default);
}
