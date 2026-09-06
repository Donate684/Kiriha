using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kiriha.Services.Data.Core;

/// <summary>
/// Enforces connection-level SQLite pragmas on every connection opened by EF Core.
/// In SQLite, pragmas like 'synchronous', 'temp_store', 'cache_size', and 'mmap_size'
/// are connection-scoped and reset when new pooled connections are opened.
/// Applying them here ensures minimum SSD write amplification and in-memory temp tables.
/// </summary>
public sealed class SqlitePragmaConnectionInterceptor : DbConnectionInterceptor
{
    private const string PragmaScript =
        "PRAGMA synchronous = NORMAL; " +
        "PRAGMA temp_store = MEMORY; " +
        "PRAGMA cache_size = -8000; " +
        "PRAGMA mmap_size = 268435456;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = PragmaScript;
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Best effort: avoid throwing if connection is a non-standard mock/memory provider
        }
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = PragmaScript;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best effort
        }
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }
}
