using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data.Core;
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

    public async Task<List<AnimeEntity>> GetAllAsync(CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entities = await context.UserAnime.AsNoTracking().ToListAsync(ct);
        Log.Information("Loaded {Count} anime/manga items from database", entities.Count);
        return entities;
    }

    public async Task<List<AnimeEntity>> GetByMediaKindAsync(MediaKind kind, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entities = await context.UserAnime.AsNoTracking().Where(x => x.MediaKind == kind).ToListAsync(ct);
        Log.Information("Loaded {Count} {Kind} items from database", entities.Count, kind);
        return entities;
    }

    public async Task UpsertAsync(AnimeEntity item, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var existing = await context.UserAnime.AsTracking().FirstOrDefaultAsync(x => x.Id == item.Id, ct);
        if (existing == null)
        {
            Log.Information("Inserting new Anime {Title} (ID: {Id})", item.Title, item.Id);
            context.UserAnime.Add(item);
        }
        else
        {
            context.Entry(existing).CurrentValues.SetValues(item);
        }
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(AnimeEntity item, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var existing = await context.UserAnime.AsTracking().FirstOrDefaultAsync(x => x.Id == item.Id, ct);
        if (existing == null)
        {
            Log.Warning("Attempted to update non-existent anime {Title} (ID: {Id})", item.Title, item.Id);
            // Fall back to upsert so the caller's intent is preserved instead of silently dropped.
            await UpsertAsync(item, ct);
            return;
        }

        Log.Information("Updating Anime {Title} (ID: {Id}). Rewatching: {Rewatch}", item.Title, item.Id, item.IsRewatching);
        context.Entry(existing).CurrentValues.SetValues(item);
        await context.SaveChangesAsync(ct);

        Log.Information("Successfully saved {Title} to database", item.Title);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var existing = await context.UserAnime.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (existing == null) return;
        context.UserAnime.Remove(existing);
        await context.SaveChangesAsync(ct);
    }

    public async Task<List<string>> GetActiveLocalImagePathsAsync(CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.UserAnime
            .AsNoTracking()
            .Where(x => !string.IsNullOrEmpty(x.LocalPosterPath))
            .Select(x => x.LocalPosterPath!)
            .ToListAsync(ct);
    }
}
