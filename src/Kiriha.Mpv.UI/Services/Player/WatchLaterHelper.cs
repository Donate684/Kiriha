using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace Kiriha.Mpv.UI.Services.Player;

public static class WatchLaterHelper
{
    public static string ComputeMpvHash(string url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(url);
        var hash = MD5.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public static double? TryGetSavedPosition(string watchLaterDir, string url)
    {
        if (string.IsNullOrWhiteSpace(watchLaterDir) || string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            var hash = ComputeMpvHash(url);
            var file = Path.Combine(watchLaterDir, hash);
            if (File.Exists(file))
            {
                var pos = ReadStartFromFile(file);
                if (pos.HasValue) return pos.Value;
            }

            if (Path.IsPathRooted(url))
            {
                try
                {
                    var full = Path.GetFullPath(url);
                    if (!string.Equals(full, url, StringComparison.Ordinal))
                    {
                        var fullHash = ComputeMpvHash(full);
                        var fullFile = Path.Combine(watchLaterDir, fullHash);
                        if (File.Exists(fullFile))
                        {
                            var pos = ReadStartFromFile(fullFile);
                            if (pos.HasValue) return pos.Value;
                        }
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to read watch-later for {Url}", url);
        }

        return null;
    }

    private static double? ReadStartFromFile(string filePath)
    {
        try
        {
            foreach (var line in File.ReadLines(filePath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("start=", StringComparison.OrdinalIgnoreCase))
                {
                    var valStr = trimmed.Substring("start=".Length).Trim();
                    if (double.TryParse(valStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pos))
                    {
                        return pos;
                    }
                }
            }
        }
        catch { }
        return null;
    }
}