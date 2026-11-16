using Kiriha.Core.Abstractions.Infrastructure;

namespace Kiriha.Infrastructure.Platform;

public sealed class ShellBrowserLauncher : IBrowserLauncher
{
    public void OpenUrl(string url) => ShellLauncher.OpenUrl(url);
}
