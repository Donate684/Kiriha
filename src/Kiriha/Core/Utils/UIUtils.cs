using Kiriha.Infrastructure.Platform;

namespace Kiriha.Core;

/// <summary>
/// Pure OS-shell navigation shim for View/ViewModel callers.
/// Localization lives exclusively behind <see cref="Kiriha.Core.Abstractions.Services.ILocalizer"/>.
/// </summary>
public static class UIUtils
{
    public static void OpenUrl(string url) => ShellLauncher.OpenUrl(url);
}
