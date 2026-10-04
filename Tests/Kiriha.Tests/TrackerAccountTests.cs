using System.Text.Json;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;

namespace Kiriha.Tests;

public sealed class TrackerAccountTests
{
    [Fact]
    public void MalTokens_InheritsFromOAuthTokens_AndClonesCorrectly()
    {
        var mal = new MalTokens
        {
            AccessToken = "mal_access",
            RefreshToken = "mal_refresh",
            ExpiresIn = 3600,
            TokenType = "Bearer",
            CreatedAt = DateTime.UtcNow
        };

        Assert.IsAssignableFrom<OAuthTokens>(mal);
        Assert.False(mal.IsExpired);

        var clone = mal.Clone();
        Assert.NotSame(mal, clone);
        Assert.Equal(mal.AccessToken, clone.AccessToken);
        Assert.Equal(mal.RefreshToken, clone.RefreshToken);
        Assert.Equal(mal.ExpiresIn, clone.ExpiresIn);
        Assert.Equal(mal.TokenType, clone.TokenType);
    }

    [Fact]
    public void ShikiTokens_PreservesMirrorAndUserId_OnClone()
    {
        var shiki = new ShikiTokens
        {
            AccessToken = "shiki_access",
            RefreshToken = "shiki_refresh",
            ExpiresIn = 7200,
            TokenType = "Bearer",
            Scope = "user_rates",
            UserId = 12345,
            Mirror = ShikiMirror.Net,
            CreatedAt = DateTime.UtcNow
        };

        Assert.IsAssignableFrom<OAuthTokens>(shiki);
        Assert.Equal(ShikiMirror.Net, shiki.Mirror);
        Assert.Equal(12345, shiki.UserId);

        var clone = shiki.Clone();
        Assert.NotSame(shiki, clone);
        Assert.Equal(shiki.AccessToken, clone.AccessToken);
        Assert.Equal(shiki.UserId, clone.UserId);
        Assert.Equal(shiki.Mirror, clone.Mirror);
        Assert.Equal(shiki.Scope, clone.Scope);
    }

    [Fact]
    public void AniListTokens_PreservesUserNameAndId_OnClone()
    {
        var anilist = new AniListTokens
        {
            AccessToken = "anilist_token",
            RefreshToken = "anilist_refresh",
            ExpiresIn = 31536000,
            TokenType = "Bearer",
            UserId = 67890,
            UserName = "KirihaFan",
            CreatedAt = DateTime.UtcNow
        };

        Assert.IsAssignableFrom<OAuthTokens>(anilist);
        Assert.Equal(67890, anilist.UserId);
        Assert.Equal("KirihaFan", anilist.UserName);

        var clone = anilist.Clone();
        Assert.NotSame(anilist, clone);
        Assert.Equal(anilist.AccessToken, clone.AccessToken);
        Assert.Equal(anilist.UserId, clone.UserId);
        Assert.Equal(anilist.UserName, clone.UserName);
    }

    [Fact]
    public void OAuthTokens_EncryptAndDecrypt_TransformsTokens()
    {
        OAuthTokens tokens = new MalTokens
        {
            AccessToken = "plain_access",
            RefreshToken = "plain_refresh"
        };

        tokens.Encrypt(raw => $"encrypted_{raw}");
        Assert.Equal("encrypted_plain_access", tokens.AccessToken);
        Assert.Equal("encrypted_plain_refresh", tokens.RefreshToken);

        tokens.Decrypt(enc => enc?.Replace("encrypted_", "") ?? "");
        Assert.Equal("plain_access", tokens.AccessToken);
        Assert.Equal("plain_refresh", tokens.RefreshToken);
    }

    [Fact]
    public void TrackerAccount_DisplayName_FormatsProperly()
    {
        var accountWithoutUser = new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.Mal
        };
        Assert.Equal("MyAnimeList", accountWithoutUser.DisplayName);

        var accountWithUser = new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.ShikiOrig,
            Username = "OtakuHero"
        };
        Assert.Equal("Shikimori (Original) (OtakuHero)", accountWithUser.DisplayName);
    }

    [Fact]
    public void TrackerAccount_Clone_PerformsDeepCopyOfTokens()
    {
        var original = new TrackerAccount
        {
            Id = "acc-1",
            TrackerId = TrackerConstants.Ids.AniList,
            Username = "Alice",
            IsEnabled = true,
            IsPrimary = true,
            IsMirror = false,
            Tokens = new AniListTokens
            {
                AccessToken = "tok1",
                UserName = "Alice"
            }
        };

        var clone = original.Clone();
        Assert.NotSame(original, clone);
        Assert.Equal(original.Id, clone.Id);
        Assert.Equal(original.TrackerId, clone.TrackerId);
        Assert.Equal(original.Username, clone.Username);
        Assert.Equal(original.IsPrimary, clone.IsPrimary);
        Assert.Equal(original.IsMirror, clone.IsMirror);

        Assert.NotNull(clone.Tokens);
        Assert.NotSame(original.Tokens, clone.Tokens);
        Assert.Equal(original.Tokens.AccessToken, clone.Tokens.AccessToken);

        // Mutating clone token does not mutate original
        clone.Tokens.AccessToken = "tok2";
        Assert.Equal("tok1", original.Tokens.AccessToken);
    }

    [Fact]
    public void TrackerAccount_PolymorphicSerialization_SerializesAndDeserializesTokens()
    {
        var accounts = new List<TrackerAccount>
        {
            new()
            {
                TrackerId = TrackerConstants.Ids.Mal,
                Username = "MalUser",
                Tokens = new MalTokens { AccessToken = "mal_tok", ExpiresIn = 3600 }
            },
            new()
            {
                TrackerId = TrackerConstants.Ids.ShikiFork,
                Username = "ShikiUser",
                Tokens = new ShikiTokens { AccessToken = "shiki_tok", Mirror = ShikiMirror.Net, UserId = 999 }
            },
            new()
            {
                TrackerId = TrackerConstants.Ids.AniList,
                Username = "AniUser",
                Tokens = new AniListTokens { AccessToken = "ani_tok", UserId = 111, UserName = "AniUser" }
            }
        };

        var json = JsonSerializer.Serialize(accounts);
        Assert.Contains("\"$type\":\"mal\"", json);
        Assert.Contains("\"$type\":\"shiki\"", json);
        Assert.Contains("\"$type\":\"anilist\"", json);

        var restored = JsonSerializer.Deserialize<List<TrackerAccount>>(json);
        Assert.NotNull(restored);
        Assert.Equal(3, restored.Count);

        var malRestored = Assert.IsType<MalTokens>(restored[0].Tokens);
        Assert.Equal("mal_tok", malRestored.AccessToken);

        var shikiRestored = Assert.IsType<ShikiTokens>(restored[1].Tokens);
        Assert.Equal("shiki_tok", shikiRestored.AccessToken);
        Assert.Equal(ShikiMirror.Net, shikiRestored.Mirror);
        Assert.Equal(999, shikiRestored.UserId);

        var aniRestored = Assert.IsType<AniListTokens>(restored[2].Tokens);
        Assert.Equal("ani_tok", aniRestored.AccessToken);
        Assert.Equal(111, aniRestored.UserId);
        Assert.Equal("AniUser", aniRestored.UserName);
    }
}
