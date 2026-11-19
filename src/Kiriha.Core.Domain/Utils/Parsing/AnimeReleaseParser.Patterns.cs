using System.Text.RegularExpressions;

namespace Kiriha.Core.Domain.Utils.Parsing;

public static partial class AnimeReleaseParser
{
    [GeneratedRegex(@"\[[0-9A-Fa-f]{8}\]|\([0-9A-Fa-f]{8}\)", RegexOptions.CultureInvariant, 500)]
    private static partial Regex Crc32Regex();

    [GeneratedRegex(@"\s+-\s+(?:mpv|VLC media player|MPC-HC|PotPlayer)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 500)]
    private static partial Regex PlayerWindowSuffixRegex();

    [GeneratedRegex(@"(?:\b|(?<=[._\-\s]))[sS](?<season>\d{1,2})[eE](?<ep1>\d{1,4}(?:\.\d)?)(?:-?[eE]?(?<ep2>\d{1,4}))?(?:v\d+)?(?:\b|(?=[._\-\s]))", RegexOptions.CultureInvariant, 500)]
    private static partial Regex SxxExxRegex();

    [GeneratedRegex(@"(?:\b|(?<=[._\-\s]))(?<season>\d{1,2})x(?<ep>\d{2,4})(?:v\d+)?(?:\b|(?=[._\-\s]))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 500)]
    private static partial Regex XFormatRegex();

    [GeneratedRegex(@"(?:\b|(?<=[._\-\s]))(?:Ep|Eps|Episode|Episodes|\u0421\u0435\u0440\u0438\u044F|\u0421\u0435\u0440|\u0412\u044B\u043F\u0443\u0441\u043A|\u042D\u043F\u0438\u0437\u043E\u0434|\u042D\u043F)\.?\s*(?<ep>\d{1,4}(?:\.\d)?)(?:v\d+)?(?:\b|(?=[._\-\s]))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 500)]
    private static partial Regex ExplicitEpRegex();

    [GeneratedRegex(@"(?:\b|(?<=[._\-\s]))(?<ep>\d{1,4}(?:\.\d)?)\s*(?:\u0441\u0435\u0440\u0438\u044F|\u044D\u043F\u0438\u0437\u043E\u0434)(?:\b|(?=[._\-\s]))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 500)]
    private static partial Regex CyrillicEpRegex();

    [GeneratedRegex(@"(?:\b|(?<=[._\-\s]))(?<ep>\d{1,4})\s*(?:\u0438\u0437|of)\s*\d{1,4}(?:\b|(?=[._\-\s]))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 500)]
    private static partial Regex ProgressEpRegex();

    [GeneratedRegex(@"(?:\s+-\s+|\s+~\s+)(?<ep1>\d{1,4}(?:\.\d)?)(?:v\d+)?(?:\s*[-~]\s*(?<ep2>\d{1,4}))?(?:\s|$|\b)", RegexOptions.CultureInvariant, 500)]
    private static partial Regex HyphenEpRegex();

    [GeneratedRegex(@"\b(?:Season|Saison|Series)\s*(?<season>\d{1,2})\b|\b(?<season>\d{1,2})(?:st|nd|rd|th)\s+Season\b|\b(?<season>\d{1,2})\s*\u0441\u0435\u0437\u043E\u043D\b|\b(?:(?:\u0422\u0412|TV))-(?<season>\d{1,2})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 500)]
    private static partial Regex ExplicitSeasonRegex();

    [GeneratedRegex(@"(?:\s+-\s+|\s+)S(?<season>\d{1,2})(?:\s+-\s+|\s+|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 500)]
    private static partial Regex StandaloneSeasonRegex();

    [GeneratedRegex(@"(?<=\s)(?<ep>\d{1,3}(?:\.\d)?)(?:v\d+)?(?=\s|$)", RegexOptions.CultureInvariant, 500)]
    private static partial Regex IsolatedNumberRegex();

    [GeneratedRegex(@"-([A-Za-z0-9_]+)$", RegexOptions.CultureInvariant, 500)]
    private static partial Regex SceneGroupRegex();

    [GeneratedRegex(@"\b(?:[A-Za-z0-9]+)\.(?:[0-9]+)\b", RegexOptions.CultureInvariant, 500)]
    private static partial Regex DottedCodecRegex();

    [GeneratedRegex(@"(?:\b|(?<=[._\-\s]))(?<type>OVA|ONA|OAD|SP|Special|Specials)(?:\b|(?=[._\-\s]))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 500)]
    private static partial Regex TitleReleaseTypeRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, 500)]
    private static partial Regex MultipleSpacesRegex();
}
