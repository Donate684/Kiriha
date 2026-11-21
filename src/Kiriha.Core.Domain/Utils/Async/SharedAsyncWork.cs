using System.Collections.Concurrent;

namespace Kiriha.Core.Domain.Utils.Async;

/// <summary>Shares in-flight work for a key. Cancelling a wait leaves the other callers' work running.</summary>
public sealed class SharedAsyncWork<TKey, TResult> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Task<TResult>> _pending = new();

    public Task<TResult> RunAsync(TKey key, Func<Task<TResult>> operation, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = _pending.GetOrAdd(key, completion.Task);
        if (ReferenceEquals(task, completion.Task))
            _ = ExecuteAsync(key, operation, completion);

        return ct.CanBeCanceled ? task.WaitAsync(ct) : task;
    }

    private async Task ExecuteAsync(TKey key, Func<Task<TResult>> operation, TaskCompletionSource<TResult> completion)
    {
        try
        {
            var result = await operation().ConfigureAwait(false);
            _pending.TryRemove(key, out _);
            completion.TrySetResult(result);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(key, out _);
            completion.TrySetException(ex);
        }
    }
}
