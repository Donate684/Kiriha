namespace Kiriha.Core.Domain.Models.Api;

public class InternalPlayerIpcMessage
{
    public const string TypeHandshake = "handshake";
    public const string TypeHandshakeAck = "handshake_ack";
    public const string TypeState = "state";
    public const string TypeMetadata = "metadata";
    public const string TypeClosed = "closed";
    public const string TypePing = "ping";
    public const string TypePong = "pong";

    public string? Type { get; set; }
    public InternalPlayerState? State { get; set; }
    public PlayerMediaMetadata? Metadata { get; set; }
    public string? Version { get; set; }
}
