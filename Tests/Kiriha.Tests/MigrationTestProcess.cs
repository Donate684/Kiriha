using System.Data.Common;
using Kiriha.Data.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kiriha.Tests;

internal static class MigrationTestProcess
{
    internal const string Command = "--migration-test-process";
    internal const string Paused = "Migration paused inside the schema transaction";
    internal const string ReplayVerified = "Rolled-back schema verified before replaying migration";

    private static async Task<int> Main(string[] args)
    {
        if (args is [PlayerIsolationTestProcess.Command, var appDirectory, .. var playerArgs])
            return await PlayerIsolationTestProcess.RunAsync(appDirectory, playerArgs);

        if (args is [SettingsTestProcess.Command, var settingsDirectory, var operation])
            return SettingsTestProcess.Run(settingsDirectory, operation);

        if (args is not [Command, var databasePath, "pause" or "resume"])
            return 2;

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
            if (args[2] == "pause")
                options.AddInterceptors(new PauseStorageMigration());
            else
                options.AddInterceptors(new VerifyMigrationReplay());

            await new DatabaseInitializer(new MigrationDbContextFactory(options.Options)).InitializeAsync();
            return 0;
        }
        catch (SqliteException ex)
        {
            await Console.Error.WriteLineAsync($"{ex.Message} Extended SQLite error code: {ex.SqliteExtendedErrorCode}.");
            return 1;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            return 1;
        }
    }

    private sealed class MigrationDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }

    private sealed class PauseStorageMigration : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.Contains("CREATE TABLE \"playback_resume\"", StringComparison.Ordinal))
                return result;

            // The preceding operations have already rebuilt metadata inside the migration transaction.
            if (command.Transaction is null)
                throw new InvalidOperationException("Expected an open schema transaction.");
            await Console.Out.WriteLineAsync(Paused);
            await Console.Out.FlushAsync(cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return result;
        }
    }

    private sealed class VerifyMigrationReplay : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.Contains("CREATE TABLE \"metadata_dg_tmp\"", StringComparison.Ordinal))
                return result;

            var transaction = command.Transaction ?? throw new InvalidOperationException("Expected an open replay transaction.");
            var connection = command.Connection ?? throw new InvalidOperationException("Expected an open replay connection.");
            await using var check = connection.CreateCommand();
            check.Transaction = transaction;
            check.CommandText = """
                SELECT NOT EXISTS (SELECT 1 FROM pragma_table_info('metadata') WHERE name = 'media_kind')
                   AND NOT EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'anilist_media_map');
                """;
            if (await check.ExecuteScalarAsync(cancellationToken) is not 1L)
                throw new InvalidOperationException("Schema changes from the killed process were not rolled back.");

            await Console.Out.WriteLineAsync(ReplayVerified);
            return result;
        }
    }
}
