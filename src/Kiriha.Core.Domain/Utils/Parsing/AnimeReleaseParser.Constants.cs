using System.Collections.Frozen;

namespace Kiriha.Core.Domain.Utils.Parsing;

public static partial class AnimeReleaseParser
{
    private static readonly FrozenSet<string> Extensions = new string[]
    {
        // Video
        ".mkv", ".mp4", ".avi", ".webm", ".m2ts", ".ts", ".wmv", ".mov", ".flv",
        // Audio
        ".mka", ".flac", ".opus", ".aac", ".mp3", ".ogg", ".ac3", ".wav",
        // Subtitles
        ".ass", ".srt", ".ssa", ".vtt", ".sub", ".idx"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> TechKeywords = new string[]
    {
        // Quality / Source
        "4k", "uhd", "2160p", "1440p", "1080p", "1080i", "720p", "576p", "480p", "360p",
        "1920x1080", "1280x720", "3840x2160", "848x480", "720x480", "640x480",
        "web-dl", "webdl", "web-rip", "webrip", "bd", "bluray", "blu-ray", "bdrip", "brrip",
        "dvd", "dvdrip", "hdtv", "tvrip", "raw", "vostfr",

        // Codecs / Formats
        "x264", "x265", "h264", "h265", "h.264", "h.265", "avc", "hevc", "av1",
        "10bit", "10-bit", "8bit", "8-bit", "hi10p", "hi10",
        "hdr", "hdr10", "hdr10+", "dv", "dovi", "sdr", "remux",

        // Audio
        "aac", "aac2.0", "aac5.1", "aacx2", "ac3", "eac3", "ddp", "ddp2.0", "ddp5.1", "dd+",
        "dts", "dts-hd", "truehd", "atmos", "flac", "flac2.0", "flac5.1", "flacx2", "opus", "mp3",
        "2.0", "5.1", "7.1", "2ch", "6ch", "8ch",

        // Providers
        "cr", "crunchyroll", "nf", "netflix", "amzn", "amazon", "hidive", "b-global", "bilibili",
        "adn", "dsnp", "abema", "funi", "funimation",

        // Subtitles & Audio info
        "multi-subs", "multisubs", "multi-sub", "multisub", "multiple subtitle", "multiple subtitles",
        "multi", "multiple", "subtitle", "subtitles", "sub", "subs", "dub", "dubbed",
        "dual-audio", "dual audio", "multi-audio", "multiaudio", "softsub", "softsubs", "hardsub", "hardsubs",

        // Editions & Versions
        "batch", "complete", "pack", "uncensored", "censored", "repack", "proper", "v2", "v3", "v4"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> TechLookup =
        TechKeywords.GetAlternateLookup<ReadOnlySpan<char>>();

    private static bool IsLanguageSuffix(ReadOnlySpan<char> span)
    {
        return span.Equals("en", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("ru", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("ja", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("eng", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("rus", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("jpn", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("ger", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("fre", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("fra", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("spa", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("ita", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("chi", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("kor", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("chs", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("cht", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("default", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("forced", StringComparison.OrdinalIgnoreCase);
    }
}
