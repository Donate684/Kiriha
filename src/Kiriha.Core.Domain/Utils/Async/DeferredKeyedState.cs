namespace Kiriha.Core.Domain.Utils.Async;

/// <summary>Serializes persistence of synchronous state edits and retains changes until they commit.</summary>
public sealed class DeferredKeyedState<T>(
    Func<Dictionary<string, T>> load,
    Func<string, T?, Task>? persist,
    Action<Exception> reportFailure) where T : struct
{
    private sealed class Change(T? value)
    {
        internal T? Value { get; } = value;
        internal Exception? Error { get; set; }
    }

    private readonly Lock _gate = new();
    private readonly Lock _loadGate = new();
    private readonly Dictionary<string, T> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Change> _pending = new(StringComparer.Ordinal);
    private bool _loaded;
    private Task? _worker;

    public bool TryGet(string key, out T value)
    {
        lock (_gate)
        {
            if (_pending.ContainsKey(key))
                return _values.TryGetValue(key, out value);
        }

        TryLoad();
        lock (_gate)
            return _values.TryGetValue(key, out value);
    }

    public void Set(string key, T value)
    {
        lock (_gate)
        {
            _values[key] = value;
            ScheduleChange(key, value);
        }
    }

    public bool Remove(string key, bool loadExisting = false)
    {
        if (loadExisting)
            TryGet(key, out _);

        lock (_gate)
        {
            bool existed = _values.Remove(key);
            // A missing in-memory key may still be in storage after a failed load or delete.
            ScheduleChange(key, null);
            return existed;
        }
    }

    private void TryLoad()
    {
        try { EnsureLoaded(); }
        catch (Exception ex) { reportFailure(ex); }
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            Task worker;
            lock (_gate)
            {
                if (_pending.Count == 0) return;
                // Cancellation limits the caller's wait. The queued changes remain owned by the writer.
                worker = _worker ??= Task.Run(DrainAsync, CancellationToken.None);
            }

            await worker.WaitAsync(ct).ConfigureAwait(false);
        }

        lock (_gate)
        {
            if (_pending.Count != 0)
                throw new IOException("Local state still has unsaved changes.", _pending.Values.FirstOrDefault(change => change.Error != null)?.Error);
        }
    }

    private void ScheduleChange(string key, T? value)
    {
        if (persist == null) return;

        _pending[key] = new Change(value);
        _worker ??= Task.Run(DrainAsync);
    }

    private void EnsureLoaded()
    {
        lock (_loadGate)
        {
            if (_loaded) return;

            var stored = load();
            lock (_gate)
            {
                foreach (var (key, value) in stored)
                {
                    if (!_pending.ContainsKey(key))
                        _values.TryAdd(key, value);
                }
            }
            _loaded = true;
        }
    }

    private async Task DrainAsync()
    {
        var write = persist;
        if (write == null) return;

        var failed = new HashSet<Change>();
        while (true)
        {
            KeyValuePair<string, Change>[] work;
            lock (_gate)
            {
                work = _pending.Where(pair => !failed.Contains(pair.Value)).ToArray();
                if (work.Length == 0)
                {
                    _worker = null;
                    return;
                }
            }

            try
            {
                EnsureLoaded();
            }
            catch (Exception ex)
            {
                reportFailure(ex);
                lock (_gate)
                {
                    foreach (var change in _pending.Values)
                        change.Error = ex;
                    _worker = null;
                }
                return;
            }

            foreach (var (key, change) in work)
            {
                lock (_gate)
                {
                    if (!ReferenceEquals(_pending.GetValueOrDefault(key), change)) continue;
                }

                try
                {
                    await write(key, change.Value).ConfigureAwait(false);
                    lock (_gate)
                    {
                        if (ReferenceEquals(_pending.GetValueOrDefault(key), change))
                            _pending.Remove(key);
                    }
                }
                catch (Exception ex)
                {
                    reportFailure(ex);
                    change.Error = ex;
                    failed.Add(change);
                }
            }
        }
    }
}
