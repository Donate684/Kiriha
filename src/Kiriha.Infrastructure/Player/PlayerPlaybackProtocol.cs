namespace Kiriha.Infrastructure.Player;

public enum PlayerLaunchStatus : byte
{
    Unavailable = 0,
    Accepted = 10,
    Ready = 11,
    Failed = 12,
    Superseded = 13,
    NotFound = 14,
    Unconfirmed = 15
}

public static class PlayerPlaybackProtocol
{
    public const string Open = "player-open-v1";
    public const string Query = "player-query-v1";
    public const string Cancel = "player-cancel-v1";
    public const string AdmissionDeadlineArg = "--playback-admit-until";
    public const string BootstrapArg = "--player-await-open";

    private static readonly TimeSpan RequestLifetime = TimeSpan.FromSeconds(12);

    public static string CreateAdmissionDeadline() =>
        (DateTimeOffset.UtcNow + RequestLifetime).ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static bool IsAdmissionExpired(string? deadline) =>
        deadline != null && (!long.TryParse(deadline, System.Globalization.CultureInfo.InvariantCulture, out var timestamp) ||
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > timestamp);

    public static string[] CreateOpen(string requestId, string[] args) =>
        [Open, requestId, PipeArgumentSerializer.Serialize(args)];

    public static string[] CreateQuery(string requestId) => [Query, requestId];

    public static string[] CreateCancel(string requestId) => [Cancel, requestId];

    public static bool IsPlaybackCommand(string[] args) =>
        args.Length > 0 && args[0] is Open or Query or Cancel;

    public static bool IsValidRequestId(string? id) =>
        id is { Length: > 0 and <= 64 } && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
