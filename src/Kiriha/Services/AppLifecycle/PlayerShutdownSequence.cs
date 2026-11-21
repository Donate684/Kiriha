using Serilog;

namespace Kiriha.Services.AppLifecycle;

/// <summary>Closes the windows before flushing the state and resources owned by the player process.</summary>
internal sealed class PlayerShutdownSequence(
    Func<IReadOnlyList<Task<bool>>> closeWindows, Func<Task<bool>> drain, TimeSpan? windowTimeout = null)
{
    private static readonly TimeSpan WindowShutdownTimeout = TimeSpan.FromSeconds(5);
    private Task<bool>? _preparation;

    internal bool IsPreparing => _preparation != null;

    // The caller owns the UI thread. Both local shutdown and IPC requests join the same preparation.
    internal Task<bool> PrepareAsync() => _preparation ??= PrepareCoreAsync();

    private async Task<bool> PrepareCoreAsync()
    {
        // Publish the preparation task before closing windows, which raises Closed synchronously.
        await Task.Yield();

        bool windowsClosed;
        try
        {
            var results = await Task.WhenAll(closeWindows()).WaitAsync(windowTimeout ?? WindowShutdownTimeout);
            windowsClosed = results.All(result => result);
        }
        catch (Exception ex)
        {
            windowsClosed = false;
            Log.Warning(ex, "Player shutdown: window cleanup failed or timed out");
        }

        try
        {
            var drained = await drain();
            return windowsClosed && drained;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Player shutdown: state and resource cleanup failed");
            return false;
        }
    }
}
