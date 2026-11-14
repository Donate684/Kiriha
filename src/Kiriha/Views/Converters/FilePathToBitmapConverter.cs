using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Kiriha.Views.Converters;

/// <summary>
/// Loads a <see cref="Bitmap"/> from a local file path. Used by the custom
/// share link icon previews so XAML can bind directly to a string path.
/// Returns <c>null</c> for empty / missing paths so <c>Image.Source</c>
/// falls through cleanly. Decoded icons are cached in a bounded dictionary
/// keyed by path + last-write timestamp to prevent unmanaged bitmap leaks.
/// </summary>
public class FilePathToBitmapConverter : IValueConverter
{
    private const int MaxCachedIcons = 64;
    private const int IconDecodeWidth = 64;

    public static readonly FilePathToBitmapConverter Instance = new();

    private static readonly ConcurrentDictionary<(string Path, long LastWriteTicks), Bitmap> IconCache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;
        if (!File.Exists(path)) return null;
        try
        {
            var fullPath = Path.GetFullPath(path);
            var ticks = File.GetLastWriteTimeUtc(fullPath).Ticks;
            var key = (fullPath, ticks);

            if (IconCache.TryGetValue(key, out var cached))
                return cached;

            if (IconCache.Count >= MaxCachedIcons)
            {
                IconCache.Clear();
            }

            // Stream-based ctor avoids holding a file handle on the icon file
            // after the bitmap is decoded — important because the user may
            // overwrite or delete the file later.
            using var stream = File.OpenRead(fullPath);
            var bitmap = Bitmap.DecodeToWidth(stream, IconDecodeWidth);
            IconCache[key] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
