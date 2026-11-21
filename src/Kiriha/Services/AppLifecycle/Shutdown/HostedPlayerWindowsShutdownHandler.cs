using Avalonia.Threading;

namespace Kiriha.Services.AppLifecycle.Shutdown;

internal sealed class HostedPlayerWindowsShutdownHandler : IShutdownHandler
{
    private readonly PlayerShutdownSequence _shutdown;

    public HostedPlayerWindowsShutdownHandler(PlayerWindowShutdown windows)
    {
        _shutdown = new PlayerShutdownSequence(windows.CloseAll, () => Task.FromResult(true));
    }

    public async Task FlushAsync() => await TryFlushAsync();

    public async Task<bool> TryFlushAsync()
    {
        var preparation = await Dispatcher.UIThread.InvokeAsync<Task<bool>>(_shutdown.PrepareAsync);
        return await preparation;
    }
}
