using System.Text.Json.Serialization;

namespace Kiriha.Core.Domain.Models.Api;

public class ShikiTokens : OAuthTokens
{
    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;

    // Shikimori specific: we might want to store user_id
    public int? UserId { get; set; }

    // Which mirror this token was issued by. Tokens are NOT cross-mirror compatible
    // because shikimori.one and shikimori.net are independent OAuth realms.
    public ShikiMirror Mirror { get; set; } = ShikiMirror.One;

    public override ShikiTokens Clone() => new()
    {
        AccessToken = AccessToken,
        RefreshToken = RefreshToken,
        ExpiresIn = ExpiresIn,
        TokenType = TokenType,
        Scope = Scope,
        CreatedAt = CreatedAt,
        UserId = UserId,
        Mirror = Mirror
    };
}
