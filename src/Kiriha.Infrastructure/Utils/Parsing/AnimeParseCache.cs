using Kiriha.Utils.Collections;

namespace Kiriha.Utils.Parsing;

/// <summary>
/// Memoizing wrapper around <see cref="AnimeReleaseParser.Parse(string)"/>.
///
/// The same filename / window title is parsed many times per session:
///   * <see cref="Kiriha.Services.Data.MappingService"/> calls Parse 4 times for
///     a single matching attempt (IsManuallyMapped, IsNegativelyMapped,
///     GetIdFromTitleAsync, SearchOnMalAsync).
///   * Anisthesia detection strategies (WindowTitle / HandleEnumeration) parse
///     the same title every detection tick.
/// A single parse is fast, but multiplied by N files × M ticks memoization
/// completely eliminates string parsing overhead.
/// </summary>
public static class AnimeParseCache
{
    private static readonly LruStringMemoizer<ParsedAnimeRelease> _cache = new(2048);

    public static ParsedAnimeRelease Parse(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return ParsedAnimeRelease.Empty;

        return _cache.GetOrAdd(filename, static key => AnimeReleaseParser.Parse(key));
    }
}
