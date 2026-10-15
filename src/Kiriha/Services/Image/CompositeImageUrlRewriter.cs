using Kiriha.Core.Abstractions.Services;

namespace Kiriha.Services.Data.Image;

/// <summary>
/// Composite image URL rewriter that applies multiple rewriters in sequence
/// (e.g. Shikimori mirror rewriter and Anime 365 mirror rewriter).
/// </summary>
public sealed class CompositeImageUrlRewriter : IImageUrlRewriter
{
    private readonly IReadOnlyList<IImageUrlRewriter> _rewriters;

    public CompositeImageUrlRewriter(IEnumerable<IImageUrlRewriter> rewriters)
    {
        ArgumentNullException.ThrowIfNull(rewriters);
        _rewriters = rewriters.ToList();
    }

    public string Rewrite(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;

        var current = url;
        foreach (var rewriter in _rewriters)
        {
            current = rewriter.Rewrite(current);
        }

        return current;
    }
}
