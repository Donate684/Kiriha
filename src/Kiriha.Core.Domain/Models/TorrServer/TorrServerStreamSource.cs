namespace Kiriha.Core.Domain.Models.TorrServer;

/// <summary>The endpoint and info hash encoded in a TorrServer playback URL.</summary>
public readonly record struct TorrServerStreamSource(string Endpoint, string Hash)
{
    public static TorrServerStreamSource? Parse(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return null;

        var path = uri.AbsolutePath;
        var route = path.LastIndexOf("/stream/", StringComparison.Ordinal);
        if (route < 0)
            return null;

        var arguments = uri.Query.TrimStart('?').Split('&');
        if (!arguments.Contains("play", StringComparer.Ordinal))
            return null;

        foreach (var argument in arguments)
        {
            if (!argument.StartsWith("link=", StringComparison.Ordinal))
                continue;

            var hash = argument[5..];
            return hash.Length is 40 or 64 && hash.All(Uri.IsHexDigit)
                ? new TorrServerStreamSource(uri.GetLeftPart(UriPartial.Authority) + path[..route], hash.ToLowerInvariant())
                : null;
        }

        return null;
    }
}
