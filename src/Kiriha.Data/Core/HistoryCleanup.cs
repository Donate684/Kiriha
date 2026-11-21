using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Data.Core;

internal static class HistoryCleanup
{
    // A delivery still in sync_tasks can be acknowledged or superseded at any time.
    // Legacy queue rows and malformed payloads have no usable history link.
    private const string PendingHistoryIds = """
        SELECT history_id
        FROM (
            SELECT CASE WHEN json_valid(payload) THEN json_extract(payload, '$.HistoryId') END AS history_id
            FROM sync_tasks
        )
        WHERE typeof(history_id) = 'integer'
        """;

    private const string PruneExpiredHistory =
        "DELETE FROM history WHERE timestamp < $cutoff AND id NOT IN (" + PendingHistoryIds + ")";
    private const string PruneAllUnreferencedHistory =
        "DELETE FROM history WHERE id NOT IN (" + PendingHistoryIds + ")";

    internal static Task<int> PruneAsync(AppDbContext context, DateTime? cutoff, CancellationToken ct) =>
        cutoff is { } expiredBefore
            ? context.Database.ExecuteSqlRawAsync(PruneExpiredHistory, [new SqliteParameter("$cutoff", expiredBefore)], ct)
            : context.Database.ExecuteSqlRawAsync(PruneAllUnreferencedHistory, ct);
}
