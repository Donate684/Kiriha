namespace Kiriha.Core.Abstractions.Services;

/// <summary>
/// Rewrites image URLs before download so that host-level redirects that were
/// discovered during API calls (e.g. shikimori.one → shikimori.me) are also
/// applied to poster/cover image requests, which travel through a separate
/// HttpClient that has no Shiki-aware retry logic.
/// </summary>
public interface IImageUrlRewriter
{
    /// <summary>
    /// Returns a (possibly rewritten) URL for the given image URL.
    /// Implementations should return the original string unchanged if no
    /// rewriting is needed, and must never throw.
    /// </summary>
    string Rewrite(string url);
}
