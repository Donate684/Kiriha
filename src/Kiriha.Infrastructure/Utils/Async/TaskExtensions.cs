using Serilog;

namespace Kiriha.Utils.Async;

public static class TaskExtensions
{
    /// <summary>
    /// Safely fire-and-forget a task, logging any unhandled exceptions
    /// instead of letting them become UnobservedTaskExceptions.
    /// </summary>
    public static async void SafeFireAndForget(this Task task, string context = "", Action<Exception>? onException = null)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation — no need to log as error
        }
        catch (Exception ex)
        {
            onException?.Invoke(ex);
            Log.Error(ex, "Unhandled exception in fire-and-forget: {Context}", context);
        }
    }
}
