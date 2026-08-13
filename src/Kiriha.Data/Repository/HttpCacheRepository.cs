using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Services.Data.Core;
using System;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data.Repository;

public sealed class HttpCacheRepository : IHttpCacheRepository
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public HttpCacheRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<HttpCacheEntry?> GetAsync(string urlHash)
    {
        if (string.IsNullOrEmpty(urlHash)) return null;
        using var context = await _contextFactory.CreateDbContextAsync();
        var entry = await context.HttpResponseCache.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UrlHash == urlHash);
        if (entry == null) return null;
        if (DateTime.UtcNow - entry.CreatedAt > Ttl) return null;
        return entry;
    }

    public async Task UpsertAsync(string urlHash, string? etag, string? lastModified, byte[] body)
    {
        if (string.IsNullOrEmpty(urlHash) || body == null) return;
        using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.HttpResponseCache
            .FirstOrDefaultAsync(e => e.UrlHash == urlHash);
        var now = DateTime.UtcNow;
        if (existing == null)
        {
            context.HttpResponseCache.Add(new HttpCacheEntry
            {
                UrlHash = urlHash,
                ETag = etag,
                LastModified = lastModified,
                Body = body,
                CreatedAt = now
            });
        }
        else
        {
            existing.ETag = etag;
            existing.LastModified = lastModified;
            existing.Body = body;
            existing.CreatedAt = now;
        }
        await context.SaveChangesAsync();
    }
}
