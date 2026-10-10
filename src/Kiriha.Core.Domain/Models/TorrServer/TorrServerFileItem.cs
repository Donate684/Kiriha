namespace Kiriha.Core.Domain.Models.TorrServer;

public sealed class TorrServerFileItem
{
    public int Id { get; init; }
    public string Path { get; init; } = string.Empty;
    public long Length { get; init; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public string FormattedSize => FormatBytes(Length);

    public bool IsVideo =>
        Path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
        Path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
        Path.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
        Path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
        Path.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ||
        Path.EndsWith(".m4v", StringComparison.OrdinalIgnoreCase) ||
        Path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F1} MB";
        return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
    }
}
