using System.Diagnostics;
using Serilog;

namespace Kiriha.Infrastructure.Platform;

/// <summary>
/// Thin wrapper over <see cref="Process.Start(ProcessStartInfo)"/> for opening
/// URLs and files via the OS shell. Windows-only: the project targets
/// <c>net11.0-windows</c>, so the previous Linux/macOS branches were dead
/// code and have been retired. Add them back behind a runtime check only if
/// the target framework changes.
/// </summary>
public static class ShellLauncher
{
    public static bool IsAllowedExternalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return false;

        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals("magnet", StringComparison.OrdinalIgnoreCase);
    }

    public static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        var trimmed = url.Trim();
        if (!IsAllowedExternalUrl(trimmed))
        {
            Log.Warning("ShellLauncher: blocked disallowed URL scheme for {Url}", url);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(trimmed) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ShellLauncher: failed to open URL {Url}", url);
        }
    }
}
