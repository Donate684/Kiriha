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
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
            var ct = cancellation.Token;
            var operation = Task.Run(() => _databaseInitializer.FlushAsync(ct), ct);
            if (!await ShutdownTimeout.WaitForAsync(operation, timeoutMs))
                Log.Warning("Shutdown flush: DatabaseInitializer.FlushAsync exceeded {Ms}ms timeout", timeoutMs);
        }
        catch (OperationCanceledException ex)
        {
            Log.Information(ex, "Shutdown flush: SQLite checkpoint deferred");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown flush: DatabaseInitializer.FlushAsync failed");
        }
    }
}
