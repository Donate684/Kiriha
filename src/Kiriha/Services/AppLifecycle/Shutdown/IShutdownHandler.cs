namespace Kiriha.Services.AppLifecycle.Shutdown;

public interface IShutdownHandler
{
    Task FlushAsync();

    async Task<bool> TryFlushAsync()
    {
        await FlushAsync();
        return true;
    }
}
