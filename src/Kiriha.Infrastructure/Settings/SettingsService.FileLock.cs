using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace Kiriha.Infrastructure.Settings;

public partial class SettingsService
{
    private static readonly TimeSpan FileLockTimeout = TimeSpan.FromSeconds(5);

    private static string GetFileLockName(string directory)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (OperatingSystem.IsWindows())
            path = path.ToUpperInvariant();
        return "Kiriha.Settings." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
    }

    private void WithFileLock(Action operation)
    {
        using var mutex = new Mutex(false, _fileLockName);
        try
        {
            if (!mutex.WaitOne(FileLockTimeout))
                throw new TimeoutException("Timed out waiting for another process to release the settings files.");
        }
        catch (AbandonedMutexException ex)
        {
            // The previous writer exited. The primary file is still the committed version.
            Log.Warning(ex, "A previous settings writer exited before releasing the file lock");
        }

        try
        {
            operation();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
