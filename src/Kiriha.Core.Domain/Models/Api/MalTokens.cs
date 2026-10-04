namespace Kiriha.Core.Domain.Models.Api;

public class MalTokens : OAuthTokens
{
    public override MalTokens Clone() => new()
    {
        AccessToken = AccessToken,
        RefreshToken = RefreshToken,
        ExpiresIn = ExpiresIn,
        TokenType = TokenType,
        CreatedAt = CreatedAt
    };
}
