using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Shared;
using Serilog;

namespace Kiriha.Core.Tracking.Auth;

public class AniListAuthService
{
    private readonly HttpClient _httpClient;

    public AniListAuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string GetAuthUrl()
    {
        return $"{AppConstants.Api.AniList.AuthUrl}?client_id={ApiKeys.AniListClientId}&redirect_uri={Uri.EscapeDataString(AppConstants.Api.RedirectUri)}&response_type=code";
    }

    public async Task<AniListTokens?> LoginAsync()
    {
        var authUrl = GetAuthUrl();
        string successMessage = UIUtils.GetLoc("auth.success", "AniList");
        string closeMessage = UIUtils.GetLoc("auth.close_window");

        var code = await OAuthHelper.AuthorizeViaLoopbackAsync(authUrl, AppConstants.Api.RedirectUri, successMessage, closeMessage);
        if (string.IsNullOrEmpty(code)) return null;

        return await ExchangeCodeForTokenAsync(code);
    }

    public async Task<AniListTokens?> LoginWithTokenAsync(string accessToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return null;

        var tokens = new AniListTokens
        {
            AccessToken = accessToken.Trim(),
            TokenType = "Bearer",
            ExpiresIn = 31536000, // AniList tokens are typically 1-year long
            CreatedAt = DateTime.UtcNow
        };

        var userInfo = await FetchUserInfoAsync(tokens.AccessToken, ct);
        if (userInfo != null)
        {
            tokens.UserId = userInfo.Value.Id;
            tokens.UserName = userInfo.Value.Name;
        }

        return tokens;
    }

    public async Task<AniListTokens?> ExchangeCodeForTokenAsync(string code, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "client_id", ApiKeys.AniListClientId },
            { "client_secret", ApiKeys.AniListClientSecret },
            { "redirect_uri", AppConstants.Api.RedirectUri },
            { "code", code }
        };

        try
        {
            using var content = new FormUrlEncodedContent(payload);
            using var response = await _httpClient.PostAsync(AppConstants.Api.AniList.TokenUrl, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                Log.Error("AniList: token exchange failed with status {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tokens = new AniListTokens
            {
                AccessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() ?? string.Empty : string.Empty,
                RefreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? string.Empty : string.Empty,
                TokenType = root.TryGetProperty("token_type", out var tt) ? tt.GetString() ?? "Bearer" : "Bearer",
                ExpiresIn = root.TryGetProperty("expires_in", out var exp) && exp.TryGetInt64(out var expVal) ? expVal : 31536000,
                CreatedAt = DateTime.UtcNow
            };

            if (string.IsNullOrEmpty(tokens.AccessToken))
            {
                Log.Error("AniList: token exchange returned empty access_token");
                return null;
            }

            var userInfo = await FetchUserInfoAsync(tokens.AccessToken, ct);
            if (userInfo != null)
            {
                tokens.UserId = userInfo.Value.Id;
                tokens.UserName = userInfo.Value.Name;
            }

            return tokens;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AniList: token exchange exception");
            return null;
        }
    }

    private async Task<(int Id, string Name)?> FetchUserInfoAsync(string accessToken, CancellationToken ct)
    {
        const string query = """
        query {
          Viewer {
            id
            name
          }
        }
        """;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, AppConstants.Api.AniList.BaseUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { query }), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("User-Agent", AppInfo.UserAgent);

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("Viewer", out var viewer) &&
                viewer.ValueKind == JsonValueKind.Object)
            {
                var id = viewer.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var idVal) ? idVal : 0;
                var name = viewer.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
                return (id, name);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AniList: failed to fetch Viewer user info");
        }

        return null;
    }
}
