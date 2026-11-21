using System.Diagnostics.CodeAnalysis;
using Kiriha.Core.Streaming.Models;

namespace Kiriha.Core.Streaming.Services;

public static class StreamingMediaUrl
{
    public static string Normalize(string url) => url.StartsWith("//", StringComparison.Ordinal) ? "https:" + url : url;

    internal static bool TryParse([NotNullWhen(true)] string? url, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        return !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(Normalize(url), UriKind.Absolute, out uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    public static bool IsDirect(string? url) => TryParse(url, out var uri) && GetFileType(uri) != null;

    public static bool IsHttp(string? url) => TryParse(url, out _);

    public static bool IsPlayable(StreamingVideo video) =>
        !string.Equals(video.VideoType, "embed", StringComparison.OrdinalIgnoreCase) &&
        TryParse(video.Url, out var uri) &&
        (video.VideoType?.ToLowerInvariant() is "m3u8" or "mp4" or "mpd" || GetFileType(uri) != null);

    internal static string? GetFileType(Uri uri) => Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() switch
    {
        ".m3u8" => "m3u8",
        ".mp4" => "mp4",
        ".mpd" => "mpd",
        ".mkv" => "mkv",
        ".webm" => "webm",
        ".ts" => "ts",
        _ => null
    };
}
