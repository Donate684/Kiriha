namespace Kiriha.Infrastructure.Platform;

/// <summary>
/// Platform feature probes. Centralised so caller code doesn't sprinkle
/// <c>OperatingSystem.IsWindows()</c> + version checks across the codebase.
/// </summary>
public static class Platform
{
    /// <summary>
    /// Mica and AcrylicBlur window backdrops are supported on Windows (Windows 10+).
    /// Windows 11 uses Mica, while Windows 10 uses AcrylicBlur.
    /// </summary>
    public static bool IsMicaSupported => OperatingSystem.IsWindows();
}
