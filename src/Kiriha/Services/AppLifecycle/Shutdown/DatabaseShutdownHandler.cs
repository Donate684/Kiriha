using Kiriha.Core.Abstractions.Services;
using Serilog;

namespace Kiriha.Services.AppLifecycle.Shutdown;

public sealed class DatabaseShutdownHandler : IShutdownHandler
{
    private readonly IDatabaseInitializer _databaseInitializer;

    public DatabaseShutdownHandler(IDatabaseInitializer databaseInitializer)
    {
        _databaseInitializer = databaseInitializer;
    }

    public async Task FlushAsync()
    {
        const int timeoutMs = 2500;
        try
        {
            var operation = _databaseInitializer.FlushAsync();
            if (!await ShutdownTimeout.WaitForAsync(operation, timeoutMs))
                Log.Warning("Shutdown flush: DatabaseInitializer.FlushAsync exceeded {Ms}ms timeout", timeoutMs);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown flush: DatabaseInitializer.FlushAsync failed");
        }
    }
}
