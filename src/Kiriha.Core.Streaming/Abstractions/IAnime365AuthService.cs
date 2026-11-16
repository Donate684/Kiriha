namespace Kiriha.Core.Streaming.Abstractions;

public sealed record Anime365LoginResult(
    bool IsSuccess,
    string? AccessToken,
    string? ErrorMessage,
    bool TokenMissing = false);

public sealed record Anime365AccountStatus(
    bool IsLoggedIn,
    string Username,
    bool IsPremium,
    string? PremiumUntil);

public interface IAnime365AuthService
{
    Task<Anime365LoginResult> LoginAsync(
        string email,
        string password,
        string? appId = null,
        CancellationToken ct = default);

    Task<Anime365AccountStatus?> GetAccountStatusAsync(
        string token,
        CancellationToken ct = default);
}
