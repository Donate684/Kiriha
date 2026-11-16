using System.Buffers;
using System.Text.RegularExpressions;
using Kiriha.Core.Domain.Utils.Collections;
using Kiriha.Core.Domain.Utils.Parsing;

namespace Kiriha.Infrastructure.Utils.Parsing;

public static partial class EmberTitleResolver
{
    private const int HeaderScanBytes = 65536;
    private static readonly LruStringMemoizer<bool> HeaderScanCache = new(256);

    public static bool IsEmberRelease(string? filePathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(filePathOrUrl))
            return false;

        if (Uri.TryCreate(filePathOrUrl, UriKind.Absolute, out var uri) && !uri.IsFile)
            return false;

        return filePathOrUrl.Contains("EMBER", StringComparison.OrdinalIgnoreCase)
            || ScanFileForEmber(filePathOrUrl);
    }

    public static bool ScanFileForEmber(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        if (Uri.TryCreate(filePath, UriKind.Absolute, out var uri) && !uri.IsFile)
            return false;

        if (!Path.IsPathRooted(filePath))
            return false;

        return HeaderScanCache.GetOrAdd(filePath, ScanFileHeaderCore);
    }

    internal static void ClearCache() => HeaderScanCache.Clear();

    private static bool ScanFileHeaderCore(string filePath)
    {
        byte[]? rented = null;
        try
        {
            if (!File.Exists(filePath))
                return false;

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            rented = ArrayPool<byte>.Shared.Rent(HeaderScanBytes);
            int bytesRead = fs.Read(rented, 0, HeaderScanBytes);

            return rented.AsSpan(0, bytesRead).IndexOf("EMBER"u8) >= 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (rented != null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    [GeneratedRegex(@"\bEMBER\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmberWordRegex();

    public static bool TryResolveDirectoryMetadata(
        string filePath,
        out string resolvedTitle,
        out string directoryName,
        out int? year,
        out int? season)
    {
        resolvedTitle = string.Empty;
        directoryName = string.Empty;
        year = null;
        season = null;

        if (!IsEmberRelease(filePath))
            return false;

        string meaningfulDir = GetMeaningfulDirectoryName(filePath);
        if (string.IsNullOrEmpty(meaningfulDir))
            return false;

        directoryName = meaningfulDir;
        var dirParsed = AnimeParseCache.Parse(meaningfulDir);
        resolvedTitle = !string.IsNullOrEmpty(dirParsed.Title) ? dirParsed.Title : meaningfulDir;
        year = dirParsed.Year;
        season = dirParsed.Season;
        return true;
    }

    public static string GetMeaningfulDirectoryName(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        while (!string.IsNullOrEmpty(dir))
        {
            string dirName = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(dirName)) break;

            string lower = dirName.ToLowerInvariant();
            if (lower == "series" || lower.StartsWith("season") || lower.StartsWith("s0") || lower == "ova" || lower == "ncop" || lower == "nced" || lower == "specials" || lower == "episodes")
            {
                dir = Path.GetDirectoryName(dir);
            }
            else
            {
                // Strip EMBER from the directory name so it doesn't get parsed as part of the anime title
                if (dirName.EndsWith("-EMBER", StringComparison.OrdinalIgnoreCase))
                    dirName = dirName[..^6].Trim();
                else if (dirName.EndsWith(" EMBER", StringComparison.OrdinalIgnoreCase))
                    dirName = dirName[..^6].Trim();
                else if (dirName.Contains("EMBER", StringComparison.OrdinalIgnoreCase))
                    dirName = EmberWordRegex().Replace(dirName, "").Trim();

                return dirName;
            }
        }
        return string.Empty;
    }
}
