using System.Text.RegularExpressions;

namespace Kiriha.Core.Streaming.Helpers;

/// <summary>
/// Redacts sensitive query parameters (such as OAuth or session tokens) from URLs before logging.
/// </summary>
public static partial class UrlRedactor
{
    public static string Redact(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return string.Empty;

        return SensitiveQueryParamRegex().Replace(url, "$1[REDACTED]");
    }

    [GeneratedRegex(@"([?&](?:access_token|token|api_key|password)=)[^&#\s]+", RegexOptions.IgnoreCase, 500)]
    private static partial Regex SensitiveQueryParamRegex();
}
