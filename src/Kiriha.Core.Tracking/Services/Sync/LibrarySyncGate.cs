namespace Kiriha.Core.Tracking.Sync;

/// <summary>
/// Coordinates write access to the user library between full tracker list reconciliations
/// (<see cref="AnimeSyncOrchestrator"/>) and background airing-metadata updates
/// (<see cref="Core.AiringInfoService"/>) so concurrent background jobs cannot overwrite
/// freshly fetched fields with stale snapshots.
/// </summary>
public sealed class LibrarySyncGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _fullSyncActive;

    public bool IsFullSyncActive => Volatile.Read(ref _fullSyncActive) == 1;

    public async Task<IDisposable> AcquireFullSyncAsync(CancellationToken ct = default)
    {
        Interlocked.Exchange(ref _fullSyncActive, 1);
        try
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            return new ReleaseScope(this, isFullSync: true);
        }
        catch
        {
            Interlocked.Exchange(ref _fullSyncActive, 0);
            throw;
        }
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

        return new ReleaseScope(this, isFullSync: false);
    }

    public void Dispose()
    {
        _gate.Dispose();
    }

    private sealed class ReleaseScope(LibrarySyncGate owner, bool isFullSync) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            if (isFullSync)
                Interlocked.Exchange(ref owner._fullSyncActive, 0);

            owner._gate.Release();
        }
    }
}
