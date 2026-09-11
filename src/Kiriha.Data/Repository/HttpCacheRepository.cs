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
        using (var zstd = new ZstandardStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            zstd.Write(data);
        }
        var compressed = ms.ToArray();
        return compressed.Length < data.Length ? compressed : data;
    }

    public static byte[] DecompressIfNeeded(byte[] data)
    {
        // 1. Zstandard frame header: 0x28, 0xB5, 0x2F, 0xFD
        if (data.Length >= 4 && data[0] == 0x28 && data[1] == 0xB5 && data[2] == 0x2F && data[3] == 0xFD)
        {
            try
            {
                using var inMs = new ReadOnlyMemoryStream(data);
                using var zstd = new ZstandardStream(inMs, CompressionMode.Decompress);
                using var outMs = new MemoryStream();
                zstd.CopyTo(outMs);
                return outMs.ToArray();
            }
            catch
            {
                return data;
            }
        }

        // 2. Legacy GZip frame header: 0x1F, 0x8B (backward compatibility for existing SQLite entries)
        if (data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B)
        {
            try
            {
                using var inMs = new ReadOnlyMemoryStream(data);
                using var gz = new GZipStream(inMs, CompressionMode.Decompress);
                using var outMs = new MemoryStream();
                gz.CopyTo(outMs);
                return outMs.ToArray();
            }
            catch
            {
                return data;
            }
        }

        return data;
    }
}
