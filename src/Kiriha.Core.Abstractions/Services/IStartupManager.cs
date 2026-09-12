namespace Kiriha.Core.Abstractions.Services;

public interface IStartupManager
{
    void EnableStartup(bool launchMinimized);
    void DisableStartup();
}
