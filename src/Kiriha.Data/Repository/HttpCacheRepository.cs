using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Services.Data.Core;
using System;
using System.IO;
using System.IO.Compression;
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

        entry.Body = DecompressIfNeeded(entry.Body);
        return entry;
    }

    public async Task UpsertAsync(string urlHash, string? etag, string? lastModified, byte[] body)
    {
        if (string.IsNullOrEmpty(urlHash) || body == null) return;
        var storedBody = Compress(body);

        using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.HttpResponseCache.AsTracking()
            .FirstOrDefaultAsync(e => e.UrlHash == urlHash);
        var now = DateTime.UtcNow;
        if (existing == null)
        {
            context.HttpResponseCache.Add(new HttpCacheEntry
            {
                UrlHash = urlHash,
                ETag = etag,
                LastModified = lastModified,
                Body = storedBody,
                CreatedAt = now
            });
        }
        else
        {
            existing.ETag = etag;
            existing.LastModified = lastModified;
            existing.Body = storedBody;
            existing.CreatedAt = now;
        }
        await context.SaveChangesAsync();
    }

    public static byte[] Compress(byte[] data)
    {
        if (data.Length < 128) return data;

        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            gz.Write(data);
        }
        var compressed = ms.ToArray();
        return compressed.Length < data.Length ? compressed : data;
    }

    public static byte[] DecompressIfNeeded(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var gz = new GZipStream(ms, CompressionMode.Decompress);
                using var outMs = new MemoryStream();
                gz.CopyTo(outMs);
                return outMs.ToArray();
            }
            catch
            {
                // Fallback in the rare event of data corruption
                return data;
            }
        }
        return data;
    }
}
