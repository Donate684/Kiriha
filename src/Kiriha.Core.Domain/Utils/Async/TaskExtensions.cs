using System.Diagnostics;

namespace Kiriha.Core.Domain.Utils.Async;

public static class TaskExtensions
{
    /// <summary>
    /// Optional logger sink wired at application startup so Domain remains dependency-free.
    /// </summary>
    public static Action<Exception, string>? UnhandledExceptionLogger { get; set; }

    /// <summary>
    /// Safely fire-and-forget a task, logging any unhandled exceptions
    /// instead of letting them become UnobservedTaskExceptions.
    /// </summary>
#pragma warning disable S3168 // Explicit SafeFireAndForget extension method for background tasks
    public static async void SafeFireAndForget(this Task task, string context = "", Action<Exception>? onException = null)
#pragma warning restore S3168
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
            if (UnhandledExceptionLogger != null)
            {
                UnhandledExceptionLogger(ex, context);
            }
            else
            {
                Trace.TraceError($"Unhandled exception in fire-and-forget ({context}): {ex}");
            }
        }
    }
}
