using Kiriha.Services.Data.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Services.Data.Repository;

public sealed partial class UserAnimeRepository : IUserAnimeRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public UserAnimeRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<AnimeEntity>> GetAllAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var entities = await context.UserAnime.AsNoTracking().ToListAsync();
        Log.Information("Loaded {Count} anime/manga items from database", entities.Count);
        return entities;
    }

    public async Task<List<AnimeEntity>> GetByMediaKindAsync(MediaKind kind)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var entities = await context.UserAnime.AsNoTracking().Where(x => x.MediaKind == kind).ToListAsync();
        Log.Information("Loaded {Count} {Kind} items from database", entities.Count, kind);
        return entities;
    }

    public async Task UpsertAsync(AnimeEntity item)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.UserAnime.FirstOrDefaultAsync(x => x.Id == item.Id);
        if (existing == null)
        {
            Log.Information("Inserting new Anime {Title} (ID: {Id})", item.Title, item.Id);
            context.UserAnime.Add(item);
        }
        else
        {
            context.Entry(existing).CurrentValues.SetValues(item);
        }
        await context.SaveChangesAsync();
    }

    public async Task UpdateAsync(AnimeEntity item)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.UserAnime.FirstOrDefaultAsync(x => x.Id == item.Id);
        if (existing == null)
        {
            Log.Warning("Attempted to update non-existent anime {Title} (ID: {Id})", item.Title, item.Id);
            // Fall back to upsert so the caller's intent is preserved instead of silently dropped.
            await UpsertAsync(item);
            return;
        }

        Log.Information("Updating Anime {Title} (ID: {Id}). Rewatching: {Rewatch}", item.Title, item.Id, item.IsRewatching);
        context.Entry(existing).CurrentValues.SetValues(item);
        await context.SaveChangesAsync();

        // Critical write: ensure new progress reaches the main .db file ASAP so a
        // Windows hard shutdown cannot leave us behind the remote tracker. PASSIVE
        // never blocks readers/writers and is cheap when the WAL is small.
        try { await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(PASSIVE);"); }
        catch (System.Exception ex) { Log.Warning(ex, "wal_checkpoint(PASSIVE) failed after updating {Title}", item.Title); }

        Log.Information("Successfully saved {Title} to database", item.Title);
    }

    public async Task DeleteAsync(int id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.UserAnime.FirstOrDefaultAsync(x => x.Id == id);
        if (existing == null) return;
        context.UserAnime.Remove(existing);
        await context.SaveChangesAsync();
    }

    public async Task<List<string>> GetActiveLocalImagePathsAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.UserAnime
            .AsNoTracking()
            .Where(x => !string.IsNullOrEmpty(x.LocalPosterPath))
            .Select(x => x.LocalPosterPath!)
            .ToListAsync();
    }
}
