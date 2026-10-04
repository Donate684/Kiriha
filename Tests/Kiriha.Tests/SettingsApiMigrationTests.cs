using System.Text.Json;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Services.Data.Settings;

namespace Kiriha.Tests;

public sealed class SettingsApiMigrationTests
{
    [Fact]
    public void EnsureMigrated_ConvertsLegacyTokensToAccounts()
    {
        var api = new AppSettings.ApiConfig
        {
            LegacyMal = new MalTokens
            {
                AccessToken = "mal_access",
                RefreshToken = "mal_refresh",
                ExpiresIn = 3600
            },
            LegacyShiki = new ShikiTokens
            {
                AccessToken = "shiki_access",
                RefreshToken = "shiki_refresh",
                Mirror = ShikiMirror.Net,
                UserId = 42
            },
            ShikiMirror = ShikiMirror.Net
        };

        Assert.Empty(api.Accounts);

        api.EnsureMigrated();

        Assert.Equal(2, api.Accounts.Count);

        var malAccount = api.GetAccount(TrackerConstants.Ids.Mal);
        Assert.NotNull(malAccount);
        Assert.True(malAccount.IsEnabled);
        Assert.True(malAccount.IsPrimary);
        Assert.True(malAccount.IsMirror);
        Assert.Equal("mal_access", malAccount.Tokens?.AccessToken);

        var shikiAccount = api.GetAccount(TrackerConstants.Ids.ShikiFork);
        Assert.NotNull(shikiAccount);
        Assert.True(shikiAccount.IsEnabled);
        Assert.False(shikiAccount.IsPrimary);
        Assert.True(shikiAccount.IsMirror);
        Assert.Equal("shiki_access", shikiAccount.Tokens?.AccessToken);

        Assert.Equal(TrackerConstants.Ids.Mal, api.PrimaryTrackerId);
    }

    [Fact]
    public void MalProperty_Bridge_UpdatesAccountsList()
    {
        var api = new AppSettings.ApiConfig();

        // 1. Setting Mal should create account in Accounts
        api.Mal = new MalTokens
        {
            AccessToken = "mal_tok_1",
            ExpiresIn = 3600
        };

        Assert.Single(api.Accounts);
        Assert.Equal(TrackerConstants.Ids.Mal, api.Accounts[0].TrackerId);
        Assert.Equal("mal_tok_1", api.Accounts[0].Tokens?.AccessToken);
        Assert.True(api.Accounts[0].IsPrimary);

        // 2. Reading Mal should return the tokens
        Assert.NotNull(api.Mal);
        Assert.Equal("mal_tok_1", api.Mal.AccessToken);

        // 3. Setting Mal to null should remove account
        api.Mal = null;
        Assert.Empty(api.Accounts);
        Assert.Null(api.Mal);
    }

    [Fact]
    public void ShikiProperty_Bridge_RespectsActiveMirror()
    {
        var api = new AppSettings.ApiConfig
        {
            ShikiMirror = ShikiMirror.Net
        };

        api.Shiki = new ShikiTokens
        {
            AccessToken = "net_tok",
            Mirror = ShikiMirror.Net,
            UserId = 100
        };

        Assert.Single(api.Accounts);
        var netAcc = api.GetAccount(TrackerConstants.Ids.ShikiFork);
        Assert.NotNull(netAcc);
        Assert.Equal("net_tok", netAcc.Tokens?.AccessToken);

        // Reading back through Shiki property
        Assert.NotNull(api.Shiki);
        Assert.Equal("net_tok", api.Shiki.AccessToken);
        Assert.Equal(ShikiMirror.Net, api.Shiki.Mirror);

        // Clearing Shiki
        api.Shiki = null;
        Assert.Empty(api.Accounts);
        Assert.Null(api.Shiki);
    }

    [Fact]
    public void PrimaryAndMirror_Accounts_Selection()
    {
        var api = new AppSettings.ApiConfig();

        api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.Mal,
            IsEnabled = true,
            IsPrimary = false,
            IsMirror = true,
            Tokens = new MalTokens { AccessToken = "mal" }
        });

        api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.AniList,
            IsEnabled = true,
            IsPrimary = true,
            IsMirror = false,
            Tokens = new AniListTokens { AccessToken = "ani" }
        });

        api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.ShikiOrig,
            IsEnabled = true,
            IsPrimary = false,
            IsMirror = true,
            Tokens = new ShikiTokens { AccessToken = "shiki" }
        });

        api.EnsurePrimaryIntegrity();

        var primary = api.GetPrimaryAccount();
        Assert.NotNull(primary);
        Assert.Equal(TrackerConstants.Ids.AniList, primary.TrackerId);

        var mirrors = api.GetMirrorAccounts().ToList();
        Assert.Equal(2, mirrors.Count);
        Assert.Contains(mirrors, m => m.TrackerId == TrackerConstants.Ids.Mal);
        Assert.Contains(mirrors, m => m.TrackerId == TrackerConstants.Ids.ShikiOrig);

        // Switch primary
        api.SetPrimaryAccount(TrackerConstants.Ids.Mal);
        Assert.Equal(TrackerConstants.Ids.Mal, api.GetPrimaryAccount()?.TrackerId);
        Assert.True(api.GetAccount(TrackerConstants.Ids.Mal)?.IsPrimary);
        Assert.False(api.GetAccount(TrackerConstants.Ids.AniList)?.IsPrimary);
    }

    [Fact]
    public void LegacyJson_AuthFile_MigratesOnLoad()
    {
        // Simulate an existing auth.json on disk from an older version
        const string oldAuthJson = """
        {
          "Mal": {
            "access_token": "old_mal_tok",
            "refresh_token": "old_mal_refresh",
            "expires_in": 3600,
            "token_type": "Bearer"
          },
          "Shiki": {
            "access_token": "old_shiki_tok",
            "refresh_token": "old_shiki_refresh",
            "expires_in": 7200,
            "token_type": "Bearer",
            "scope": "user_rates",
            "UserId": 55,
            "Mirror": 0
          },
          "ShikiMirror": 0
        }
        """;

        var loaded = JsonSerializer.Deserialize(oldAuthJson, AppSettingsJsonContext.Default.ApiConfig);
        Assert.NotNull(loaded);

        loaded.EnsureMigrated();

        Assert.Equal(2, loaded.Accounts.Count);
        Assert.NotNull(loaded.Mal);
        Assert.Equal("old_mal_tok", loaded.Mal.AccessToken);
        Assert.NotNull(loaded.Shiki);
        Assert.Equal("old_shiki_tok", loaded.Shiki.AccessToken);
    }

    [Fact]
    public void SettingsService_Roundtrip_PersistsAccountsAndTokens()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Kiriha_Test_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var service = new SettingsService(tempDir))
            {
                service.Update(settings =>
                {
                    settings.Api.Accounts.Add(new TrackerAccount
                    {
                        TrackerId = TrackerConstants.Ids.AniList,
                        Username = "TestUser",
                        IsEnabled = true,
                        IsPrimary = true,
                        IsMirror = true,
                        Tokens = new AniListTokens
                        {
                            AccessToken = "secret_access_ani",
                            RefreshToken = "secret_refresh_ani",
                            UserName = "TestUser"
                        }
                    });
                    settings.Api.Mal = new MalTokens
                    {
                        AccessToken = "secret_access_mal",
                        RefreshToken = "secret_refresh_mal"
                    };
                }, SettingsSection.Api, save: false);

                service.SaveImmediate();
            }

            // Reload from disk
            using var reloaded = new SettingsService(tempDir);

            Assert.Equal(2, reloaded.Current.Api.Accounts.Count);

            var ani = reloaded.Current.Api.GetAccount(TrackerConstants.Ids.AniList);
            Assert.NotNull(ani);
            Assert.Equal("TestUser", ani.Username);
            Assert.True(ani.IsPrimary);
            Assert.Equal("secret_access_ani", ani.Tokens?.AccessToken);

            var mal = reloaded.Current.Api.GetAccount(TrackerConstants.Ids.Mal);
            Assert.NotNull(mal);
            Assert.Equal("secret_access_mal", mal.Tokens?.AccessToken);
            Assert.NotNull(reloaded.Current.Api.Mal);
            Assert.Equal("secret_access_mal", reloaded.Current.Api.Mal.AccessToken);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }
}
