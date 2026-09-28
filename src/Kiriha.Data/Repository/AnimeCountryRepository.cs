using System.Data;
using System.Data.Common;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Services.Data.Core;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Services.Data.Repository;

public sealed class AnimeCountryRepository : IAnimeCountryRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public AnimeCountryRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<Dictionary<int, string>> GetBatchAsync(IEnumerable<int> malIds, CancellationToken ct = default)
    {
        var idList = malIds.Distinct().ToList();
        var result = new Dictionary<int, string>();
        if (idList.Count == 0) return result;

        try
        {
            using var context = await _contextFactory.CreateDbContextAsync(ct);
            var connection = context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(ct);
            }

            // SQLite supports up to 999 parameters; chunk by 500 to be safe
            foreach (var chunk in idList.Chunk(500))
            {
                using var command = connection.CreateCommand();
                var paramNames = new List<string>(chunk.Length);
                for (int i = 0; i < chunk.Length; i++)
                {
                    string pName = $"@p{i}";
                    paramNames.Add(pName);
                    var param = command.CreateParameter();
                    param.ParameterName = pName;
                    param.Value = chunk[i];
                    command.Parameters.Add(param);
                }

                command.CommandText = $"SELECT mal_id, country_code FROM anime_country_origin WHERE mal_id IN ({string.Join(",", paramNames)})";
                using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    int malId = reader.GetInt32(0);
                    string code = reader.GetString(1);
                    result[malId] = code;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AnimeCountryRepository: GetBatchAsync failed");
        }

        return result;
    }

    public async Task UpsertBatchAsync(IReadOnlyDictionary<int, string> countries, CancellationToken ct = default)
    {
        if (countries.Count == 0) return;

        try
        {
            using var context = await _contextFactory.CreateDbContextAsync(ct);
            var connection = context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(ct);
            }

            using var transaction = connection.BeginTransaction();
            string now = DateTime.UtcNow.ToString("O");

            foreach (var kvp in countries)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO anime_country_origin (mal_id, country_code, fetched_at)
                    VALUES (@mal_id, @country_code, @fetched_at)
                    ON CONFLICT(mal_id) DO UPDATE SET
                        country_code = excluded.country_code,
                        fetched_at = excluded.fetched_at;";

                var pId = command.CreateParameter();
                pId.ParameterName = "@mal_id";
                pId.Value = kvp.Key;
                command.Parameters.Add(pId);

                var pCode = command.CreateParameter();
                pCode.ParameterName = "@country_code";
                pCode.Value = kvp.Value;
                command.Parameters.Add(pCode);

                var pFetched = command.CreateParameter();
                pFetched.ParameterName = "@fetched_at";
                pFetched.Value = now;
                command.Parameters.Add(pFetched);

                await command.ExecuteNonQueryAsync(ct);
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AnimeCountryRepository: UpsertBatchAsync failed");
        }
    }
}
