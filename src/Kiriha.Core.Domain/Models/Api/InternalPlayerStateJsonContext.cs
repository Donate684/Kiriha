using System.Text.Json.Serialization;

namespace Kiriha.Core.Domain.Models.Api;

[JsonSerializable(typeof(InternalPlayerState))]
[JsonSerializable(typeof(InternalPlayerIpcMessage))]
[JsonSerializable(typeof(PlayerMediaMetadata))]
public partial class InternalPlayerStateJsonContext : JsonSerializerContext
{
}
