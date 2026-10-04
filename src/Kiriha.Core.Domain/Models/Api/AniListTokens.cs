using System.Text.Json.Serialization;

namespace Kiriha.Core.Domain.Models.Api;

public class AniListTokens : OAuthTokens
{
    [JsonPropertyName("user_id")]
    public int? UserId { get; set; }

    [JsonPropertyName("user_name")]
    public string? UserName { get; set; }

    public override AniListTokens Clone() => new()
    {
        AccessToken = AccessToken,
        RefreshToken = RefreshToken,
        ExpiresIn = ExpiresIn,
        TokenType = TokenType,
        CreatedAt = CreatedAt,
        UserId = UserId,
        UserName = UserName
    };
}
