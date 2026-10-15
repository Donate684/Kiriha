using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kiriha.Core.Streaming.Helpers;

public static class AnimeLibTokenHelper
{
    /// <summary>
    /// Extracts and cleans a raw JWT token from various user inputs:
    /// raw JWT, "Bearer <token>", JSON object with access_token/token/auth_token, or quoted strings.
    /// </summary>
    public static string CleanToken(string? rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
            return string.Empty;

        var cleaned = rawInput.Trim().Trim('"', '\'', '`');
        if (cleaned.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned["Bearer ".Length..].Trim().Trim('"', '\'', '`');
        }

        if (cleaned.StartsWith('{') && cleaned.EndsWith('}'))
        {
            try
            {
                using var doc = JsonDocument.Parse(cleaned);
                var root = doc.RootElement;
                if (root.TryGetProperty("access_token", out var atProp) && atProp.ValueKind == JsonValueKind.String)
                {
                    cleaned = atProp.GetString() ?? cleaned;
                }
                else if (root.TryGetProperty("token", out var tProp) && tProp.ValueKind == JsonValueKind.String)
                {
                    cleaned = tProp.GetString() ?? cleaned;
                }
                else if (root.TryGetProperty("auth_token", out var authProp) && authProp.ValueKind == JsonValueKind.String)
                {
                    cleaned = authProp.GetString() ?? cleaned;
                }
                else if (root.TryGetProperty("auth", out var authObj) && authObj.ValueKind == JsonValueKind.Object)
                {
                    if (authObj.TryGetProperty("access_token", out var innerAt) && innerAt.ValueKind == JsonValueKind.String)
                        cleaned = innerAt.GetString() ?? cleaned;
                    else if (authObj.TryGetProperty("token", out var innerToken) && innerToken.ValueKind == JsonValueKind.String)
                        cleaned = innerToken.GetString() ?? cleaned;
                }
            }
            catch
            {
                var match = Regex.Match(cleaned, @"""access_token""\s*:\s*""([^""]+)""");
                if (match.Success)
                {
                    cleaned = match.Groups[1].Value;
                }
                else
                {
                    var tokenMatch = Regex.Match(cleaned, @"""token""\s*:\s*""([^""]+)""");
                    if (tokenMatch.Success)
                    {
                        cleaned = tokenMatch.Groups[1].Value;
                    }
                }
            }
        }

        cleaned = cleaned.Trim().Trim('"', '\'', '`');
        if (cleaned.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned["Bearer ".Length..].Trim().Trim('"', '\'', '`');
        }

        return cleaned;
    }

    /// <summary>
    /// Attempts to extract username or user identifier from JWT claims (sub, username, name, email).
    /// </summary>
    public static string? TryExtractUsername(string? rawToken)
    {
        var token = CleanToken(rawToken);
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
                return null;

            var base64 = parts[1].Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("username", out var u) && !string.IsNullOrWhiteSpace(u.GetString()))
                return u.GetString();

            if (root.TryGetProperty("name", out u) && !string.IsNullOrWhiteSpace(u.GetString()))
                return u.GetString();

            if (root.TryGetProperty("nickname", out u) && !string.IsNullOrWhiteSpace(u.GetString()))
                return u.GetString();

            if (root.TryGetProperty("email", out u) && !string.IsNullOrWhiteSpace(u.GetString()))
                return u.GetString();

            if (root.TryGetProperty("sub", out u) && !string.IsNullOrWhiteSpace(u.GetString()))
                return $"ID: {u.GetString()}";
        }
        catch
        {
            // Ignore JWT decoding failure
        }

        return null;
    }
}
