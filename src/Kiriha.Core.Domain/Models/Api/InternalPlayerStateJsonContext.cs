using System.Text.Json.Serialization;

namespace Kiriha.Core.Domain.Models.Api;

[JsonSerializable(typeof(InternalPlayerState))]
public partial class InternalPlayerStateJsonContext : JsonSerializerContext
{
}
