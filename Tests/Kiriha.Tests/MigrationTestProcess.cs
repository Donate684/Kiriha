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

    private static async Task<int> Main(string[] args)
    {
        if (args is not [Command, var databasePath, "pause" or "resume"])
            return 2;

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
            if (args[2] == "pause")
                options.AddInterceptors(new PauseStorageMigration());

            await new DatabaseInitializer(new MigrationDbContextFactory(options.Options)).InitializeAsync();
            return 0;
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
}
