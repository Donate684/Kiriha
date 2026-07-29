using System;
using System.Threading.Tasks;
using Kiriha.Services.Data.Core;
using Serilog;

namespace Kiriha.Services.AppLifecycle.Shutdown;

public sealed class DatabaseShutdownHandler : IShutdownHandler
{
    private readonly DatabaseInitializer _databaseInitializer;

    public DatabaseShutdownHandler(DatabaseInitializer databaseInitializer)
    {
        _databaseInitializer = databaseInitializer;
    }

    public async Task FlushAsync()
    {
        const int timeoutMs = 2500;
        try
        {
            var operation = _databaseInitializer.FlushAsync();
            if (!await WaitForAsync(operation, timeoutMs))
                Log.Warning("Shutdown flush: DatabaseInitializer.FlushAsync exceeded {Ms}ms timeout", timeoutMs);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown flush: DatabaseInitializer.FlushAsync failed");
        }
    }

    private static async Task<bool> WaitForAsync(Task operation, int timeoutMs)
    {
        try
        {
            await operation.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
