using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace Kiriha.Infrastructure.TorrServer;

/// <summary>Coordinates preparation, playback ownership and removal between Kiriha processes.</summary>
internal sealed class TorrServerUsage(
    string directory, TimeSpan releaseDelay, Func<string, string, CancellationToken, Task<bool>> remove)
{
    private const int SharingRetryDelayMs = 25;
    private static readonly TimeSpan EndpointLockTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(5);
    private readonly Lock _pendingGate = new();
    private readonly HashSet<Task> _pendingReleases = [];

    internal async Task<FileStream> LockEndpointAsync(string endpoint, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var filename = Path.Combine(directory, EndpointKey(endpoint) + ".lock");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(EndpointLockTimeout);
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                try
                {
                    return new FileStream(filename, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException ex) when (IsSharingViolation(ex))
                {
                    await Task.Delay(SharingRetryDelayMs, deadline.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out waiting for TorrServer torrent operations.", ex);
        }
    }

    // The caller holds the endpoint lock. The read handle stays open for the owner's lifetime.
    internal IDisposable AcquireLocked(string endpoint, string hash)
    {
        var path = GetUsagePath(endpoint, hash);
        if (!File.Exists(path))
        {
            using var marker = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }

        var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new Lease(reader, () => ScheduleRelease(endpoint, hash));
    }

    internal async Task<bool> RemoveIfUnusedAsync(string endpoint, string hash, bool requireMarker, CancellationToken ct)
    {
        await using var gate = await LockEndpointAsync(endpoint, ct).ConfigureAwait(false);
        var path = GetUsagePath(endpoint, hash);
        try
        {
            using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (FileNotFoundException) when (requireMarker)
        {
            return true;
        }
        catch (FileNotFoundException)
        {
            // An older caller can request cleanup without having acquired a lease.
            return await remove(endpoint, hash, ct).ConfigureAwait(false);
        }
        catch (IOException ex) when (IsSharingViolation(ex))
        {
            return true;
        }

        // The endpoint gate keeps new leases and additions out until removal has finished.
        if (!await remove(endpoint, hash, ct).ConfigureAwait(false))
            return false;

        File.Delete(path);
        return true;
    }

    internal async Task FlushAsync(CancellationToken ct)
    {
        Task[] pending;
        lock (_pendingGate)
            pending = [.. _pendingReleases];

        await Task.WhenAll(pending).WaitAsync(ct).ConfigureAwait(false);
    }

    private void ScheduleRelease(string endpoint, string hash)
    {
        lock (_pendingGate)
        {
            var pending = Task.Run(() => ReleaseAsync(endpoint, hash));
            _pendingReleases.Add(pending);
            _ = pending.ContinueWith(
                completed =>
                {
                    lock (_pendingGate)
                        _pendingReleases.Remove(completed);
                },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private async Task ReleaseAsync(string endpoint, string hash)
    {
        using var deadline = new CancellationTokenSource(ReleaseTimeout + releaseDelay);
        try
        {
            await Task.Delay(releaseDelay, deadline.Token).ConfigureAwait(false);
            if (!await RemoveIfUnusedAsync(endpoint, hash, requireMarker: true, deadline.Token).ConfigureAwait(false))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), deadline.Token).ConfigureAwait(false);
                await RemoveIfUnusedAsync(endpoint, hash, requireMarker: true, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to release torrent {Hash} at {Endpoint}", hash, endpoint);
        }
    }

    private string GetUsagePath(string endpoint, string hash) =>
        Path.Combine(directory, EndpointKey(endpoint) + "." + Fingerprint(hash.Trim().ToUpperInvariant()) + ".usage");

    private static string EndpointKey(string endpoint)
    {
        var uri = new Uri(endpoint, UriKind.Absolute);
        var host = uri.IsLoopback ? "loopback" : uri.IdnHost;
        return Fingerprint($"{uri.Scheme}://{host}:{uri.Port}{uri.AbsolutePath.TrimEnd('/')}");
    }

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool IsSharingViolation(IOException exception) =>
        (exception.HResult & 0xFFFF) is 11 or 32 or 33;

    private sealed class Lease(FileStream reader, Action released) : IDisposable
    {
        private FileStream? _reader = reader;

        public void Dispose()
        {
            var handle = Interlocked.Exchange(ref _reader, null);
            if (handle == null)
                return;

            handle.Dispose();
            released();
        }
    }
}
