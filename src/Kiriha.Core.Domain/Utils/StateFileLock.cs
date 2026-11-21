using System.Security.Cryptography;
using System.Text;

namespace Kiriha.Core.Domain.Utils;

/// <summary>Coordinates synchronous read-modify-write operations on a state file across processes.</summary>
public sealed class StateFileLock : IDisposable
{
    private readonly Mutex _mutex;

    private StateFileLock(Mutex mutex) => _mutex = mutex;

    public static StateFileLock Acquire(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
            fullPath = fullPath.ToUpperInvariant();

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)));
        var mutex = new Mutex(false, "Kiriha.State." + hash);
        try
        {
            try
            {
                if (!mutex.WaitOne(TimeSpan.FromSeconds(1)))
                    throw new IOException($"Timed out waiting for state file '{path}'.");

                return new StateFileLock(mutex);
            }
            catch (AbandonedMutexException)
            {
                // The previous writer exited. The mutex now belongs to this thread.
                return new StateFileLock(mutex);
            }
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
