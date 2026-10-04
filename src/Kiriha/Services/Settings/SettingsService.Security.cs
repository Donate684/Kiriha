using System.Security.Cryptography;
using System.Text;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Serilog;

namespace Kiriha.Services.Data.Settings;

public partial class SettingsService
{
    private void EncryptApiConfig(AppSettings.ApiConfig api)
    {
        foreach (var account in api.Accounts)
        {
            account.Tokens?.Encrypt(Protect);
        }
        api.LegacyMal?.Encrypt(Protect);
        api.LegacyShiki?.Encrypt(Protect);
    }

    private void DecryptApiConfig(AppSettings.ApiConfig api)
    {
        if (api.LegacyMal != null)
        {
            api.LegacyMal.Decrypt(Unprotect);
            if (string.IsNullOrEmpty(api.LegacyMal.AccessToken)) api.LegacyMal = null;
        }
        if (api.LegacyShiki != null)
        {
            api.LegacyShiki.Decrypt(Unprotect);
            if (string.IsNullOrEmpty(api.LegacyShiki.AccessToken)) api.LegacyShiki = null;
        }

        for (int i = api.Accounts.Count - 1; i >= 0; i--)
        {
            var acc = api.Accounts[i];
            if (acc.Tokens != null)
            {
                acc.Tokens.Decrypt(Unprotect);
                if (string.IsNullOrEmpty(acc.Tokens.AccessToken))
                {
                    acc.Tokens = null;
                }
            }
        }

        api.EnsureMigrated();
    }

    /// <summary>
    /// DPAPI-encrypts an OAuth token for at-rest storage. Returns an empty string
    /// on any failure mode (non-Windows, missing user profile, broken keychain),
    /// which forces the user to re-authenticate rather than silently dumping
    /// plaintext tokens to disk. Better to lose the session than to leak it.
    /// </summary>
    private string Protect(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        if (!OperatingSystem.IsWindows())
        {
            Log.Error("SettingsService: token encryption requires Windows DPAPI; refusing to persist plaintext");
            return string.Empty;
        }

        try
        {
            var data = Encoding.UTF8.GetBytes(text);
            var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsService: DPAPI Protect failed; tokens will not be persisted");
            return string.Empty;
        }
    }

    /// <summary>
    /// DPAPI-decrypts an OAuth token. On any failure (different user, corrupted
    /// blob, non-Windows host) returns empty so the consumer treats it as "no
    /// saved token" and triggers a fresh OAuth flow.
    /// </summary>
    private string Unprotect(string? encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText)) return string.Empty;

        if (!OperatingSystem.IsWindows())
        {
            Log.Warning("SettingsService: cannot decrypt tokens off-Windows; user must re-authenticate");
            return string.Empty;
        }

        try
        {
            var encrypted = Convert.FromBase64String(encryptedText);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SettingsService: DPAPI Unprotect failed; treating saved token as missing");
            return string.Empty;
        }
    }
}
