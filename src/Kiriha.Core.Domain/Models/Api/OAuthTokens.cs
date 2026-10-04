using System.Text.Json.Serialization;

namespace Kiriha.Core.Domain.Models.Api;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(MalTokens), "mal")]
[JsonDerivedType(typeof(ShikiTokens), "shiki")]
[JsonDerivedType(typeof(AniListTokens), "anilist")]
public abstract class OAuthTokens
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public long ExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsExpired => DateTime.UtcNow >= CreatedAt.AddSeconds(ExpiresIn - 60);

    public virtual void Encrypt(Func<string?, string> protect)
    {
        AccessToken = protect(AccessToken);
        RefreshToken = protect(RefreshToken);
    }

    public virtual void Decrypt(Func<string?, string> unprotect)
    {
        AccessToken = unprotect(AccessToken);
        RefreshToken = unprotect(RefreshToken);
    }

    public abstract OAuthTokens Clone();
}
