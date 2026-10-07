using System.Text.RegularExpressions;

namespace Kiriha.Utils.Parsing;

public static partial class EmberTitleResolver
{
    public static bool ScanFileForEmber(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[65536]; // Read first 64KB
            int bytesRead = fs.Read(buffer, 0, buffer.Length);

            return buffer.AsSpan(0, bytesRead).IndexOf("EMBER"u8) >= 0;
        }
        catch
        {
            return false;
        }
    }

    [GeneratedRegex(@"\bEMBER\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmberWordRegex();

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
                else if (dirName.IndexOf("EMBER", StringComparison.OrdinalIgnoreCase) >= 0)
                    dirName = EmberWordRegex().Replace(dirName, "").Trim();

                return dirName;
            }
        }
        return string.Empty;
    }
}
