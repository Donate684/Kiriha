namespace Kiriha.Core.Abstractions.Infrastructure;

/// <summary>
/// Launches external URLs in the user's default system browser.
/// </summary>
public interface IBrowserLauncher
{
    void OpenUrl(string url);
}
