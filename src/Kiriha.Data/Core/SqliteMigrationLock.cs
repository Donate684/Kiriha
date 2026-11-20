using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Serilog;

namespace Kiriha.Data.Core;

/// <summary>
/// Records ownership of EF Core's SQLite migration lock in a file held exclusively
/// by the migrating process. The OS releases the file handle even if the process dies.
/// </summary>
internal sealed class SqliteMigrationLock : IAsyncDisposable
{
    private const int OwnerByteCount = 16;
    private readonly FileStream _ownerFile;

    private SqliteMigrationLock(FileStream ownerFile)
    {
        _ownerFile = ownerFile;
    }

    internal static SqliteMigrationLock? Open(SqliteConnection connection)
    {
        // SQLite reports the absolute filename on an open connection and an empty name for memory databases.
        var databasePath = connection.DataSource;
        if (string.IsNullOrEmpty(databasePath) || databasePath == ":memory:")
            return null;

        var ownerFile = new FileStream(databasePath + ".migration-lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, bufferSize: 4096, useAsync: true);
        return new SqliteMigrationLock(ownerFile);
    }

    internal async Task RecoverAsync(SqliteConnection connection)
    {
        if (_ownerFile.Length != OwnerByteCount)
            return;

        var previousOwner = new byte[OwnerByteCount];
        await _ownerFile.ReadExactlyAsync(previousOwner);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsLock';
            """;
        if (await command.ExecuteScalarAsync() is not 1L)
            return;

        // An EF tool that does not use our file lock can hold a different row.
        // Remove only the row whose token this file proves was written by an earlier Kiriha process.
        command.CommandText = """
            DELETE FROM "__EFMigrationsLock" WHERE "Id" = 1 AND "Timestamp" = $owner;
            """;
        command.Parameters.AddWithValue("$owner", new Guid(previousOwner).ToString("N"));
        if (await command.ExecuteNonQueryAsync() > 0)
            Log.Warning("Recovered an abandoned Kiriha SQLite migration lock");
    }

    private async Task<string> RecordOwnerAsync(CancellationToken ct)
    {
        var owner = Guid.NewGuid();
        _ownerFile.Position = 0;
        await _ownerFile.WriteAsync(owner.ToByteArray(), ct);
        _ownerFile.SetLength(OwnerByteCount);
        await _ownerFile.FlushAsync(ct);
        return owner.ToString("N");
    }

    internal void ClearOwner() => _ownerFile.SetLength(0);

    public ValueTask DisposeAsync() => _ownerFile.DisposeAsync();

    internal sealed class OwnerInterceptor : DbCommandInterceptor
    {
        internal static OwnerInterceptor Instance { get; } = new();

        private const string ProviderLockInsert = """INSERT OR IGNORE INTO "__EFMigrationsLock"(""";
        private const string OwnedLockInsert = """
            INSERT OR IGNORE INTO "__EFMigrationsLock"("Id", "Timestamp") VALUES (1, $owner);
            SELECT changes();
            """;

        public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.CommandSource == CommandSource.Migrations &&
                eventData.Context is AppDbContext { ActiveMigrationLock: { } migrationLock } &&
                command.CommandText.StartsWith(ProviderLockInsert, StringComparison.Ordinal))
            {
                // EF's SQLite provider uses Timestamp as opaque text. Write our token to disk
                // before EF inserts it so a process exit at any later point is recoverable.
                var owner = await migrationLock.RecordOwnerAsync(cancellationToken);
                var parameter = command.CreateParameter();
                parameter.ParameterName = "$owner";
                parameter.Value = owner;
                command.Parameters.Add(parameter);
                command.CommandText = OwnedLockInsert;
            }

            return result;
        }

        public override ValueTask<object?> ScalarExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, object? result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is AppDbContext { ActiveMigrationLock: not null } &&
                command.CommandText == OwnedLockInsert && result is 0L)
            {
                throw new InvalidOperationException(
                    "SQLite migration is blocked by a lock whose owner Kiriha cannot verify.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
