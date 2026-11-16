using Kiriha.Core.Domain.Models;

namespace Kiriha.Mpv.UI.Services.Player;

public interface IPlayerMediaMetadataResolver
{
    PlayerMediaMetadata Resolve(string videoPath);
    PlayerMediaMetadata? ResolveByTitle(string title, string? episodeText = null) => null;
}
