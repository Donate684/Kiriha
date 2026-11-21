using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SQLitePCL;

namespace Kiriha.Data.Core;

internal sealed class SqliteMaintenanceConnection(AppDbContext context, SqliteConnection connection, int defaultTimeout)
    : IAsyncDisposable
{
    internal const int Busy = 5;
    internal const int Locked = 6;
    internal const int Interrupted = 9;
    private const int LockTimeoutSeconds = 1;

    internal static async Task<SqliteMaintenanceConnection> OpenAsync(AppDbContext context, CancellationToken ct)
    {
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        var previousTimeout = connection.DefaultTimeout;
        connection.DefaultTimeout = LockTimeoutSeconds;
        context.Database.SetCommandTimeout(LockTimeoutSeconds);
        try
        {
            await context.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
            return new SqliteMaintenanceConnection(context, connection, previousTimeout);
        }
        catch
        {
            connection.DefaultTimeout = previousTimeout;
            throw;
        }
    }

    internal IDisposable InterruptWhenCancelled(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var handle = connection.Handle ?? throw new InvalidOperationException("SQLite maintenance requires an open connection.");
        raw.sqlite3_progress_handler(handle, 1000, static state => state is CancellationToken { IsCancellationRequested: true } ? 1 : 0, ct);
        return new ProgressHandler(handle);
    }

    public async ValueTask DisposeAsync()
    {
        connection.DefaultTimeout = defaultTimeout;
        await context.Database.CloseConnectionAsync().ConfigureAwait(false);
    }

    private sealed class ProgressHandler(sqlite3 handle) : IDisposable
    {
        public void Dispose() => raw.sqlite3_progress_handler(handle, 0, null, null);
    }
}
