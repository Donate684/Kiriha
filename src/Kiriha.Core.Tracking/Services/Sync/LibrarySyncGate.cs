using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Tracking.Sync;

/// <summary>
/// Coordinates full list reconciliation, background metadata updates, and local library writes.
/// </summary>
public sealed class LibrarySyncGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _progressGate = new(1, 1);
    private readonly Lock _progressChangesLock = new();
    private Dictionary<(int Id, MediaKind Kind), bool>? _entryChangesDuringSync;
    private int _fullSyncRequests;

    public bool IsFullSyncActive => Volatile.Read(ref _fullSyncRequests) > 0;

    public async Task<IDisposable> AcquireFullSyncAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref _fullSyncRequests);
        try
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Decrement(ref _fullSyncRequests);
            throw;
        }
        lock (_progressChangesLock)
            _entryChangesDuringSync = [];
        return new ReleaseScope(_gate, EndFullSync);
    }

    public async Task<IDisposable?> TryAcquireBackgroundUpdateAsync(CancellationToken ct = default)
    {
        if (IsFullSyncActive)
            return null;

        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
            return null;

        if (IsFullSyncActive)
        {
            _gate.Release();
            return null;
        }

        return new ReleaseScope(_gate);
    }

    internal async Task<IDisposable> AcquireProgressWriteAsync(CancellationToken ct)
    {
        await _progressGate.WaitAsync(ct).ConfigureAwait(false);
        return new ReleaseScope(_progressGate);
    }

    internal void RecordProgressChange(int id, MediaKind kind)
    {
        lock (_progressChangesLock)
            _entryChangesDuringSync?[(id, kind)] = false;
    }

    internal void RecordEntryRemoval(int id, MediaKind kind)
    {
        lock (_progressChangesLock)
            _entryChangesDuringSync?[(id, kind)] = true;
    }

    internal Dictionary<(int Id, MediaKind Kind), bool> GetEntryChangesDuringSync()
    {
        lock (_progressChangesLock)
            return _entryChangesDuringSync is { } changes ? new(changes) : [];
    }

    private void EndFullSync()
    {
        lock (_progressChangesLock)
            _entryChangesDuringSync = null;
        Interlocked.Decrement(ref _fullSyncRequests);
    }

    public void Dispose()
    {
        _gate.Dispose();
        _progressGate.Dispose();
    }

    private sealed class ReleaseScope(SemaphoreSlim gate, Action? beforeRelease = null) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            beforeRelease?.Invoke();
            gate.Release();
        }
    }
}
