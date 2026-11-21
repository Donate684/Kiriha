using System.IO.Compression;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Data.Core;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Data.Repository;

public sealed class HttpCacheRepository : IHttpCacheRepository
{
    internal static readonly TimeSpan Retention = TimeSpan.FromDays(60);

    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _writes = new(1, 1);

    public HttpCacheRepository(IDbContextFactory<AppDbContext> contextFactory, TimeProvider? clock = null)
    {
        _contextFactory = contextFactory;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<HttpCacheEntry?> GetAsync(string urlHash, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(urlHash)) return null;
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entry = await context.HttpResponseCache.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UrlHash == urlHash, ct);
        if (entry is null) return null;
        if (_clock.GetUtcNow().UtcDateTime - entry.CreatedAt > Retention) return null;

        entry.Body = DecompressIfNeeded(entry.Body);
        return entry;
    }

    public async Task UpsertAsync(string urlHash, string? etag, string? lastModified, byte[] body, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(urlHash) || body is null) return;
        await _writes.WaitAsync(ct);
        try
        {
            await StoreAsync(urlHash, etag, lastModified, body, ct);
        }
        finally
        {
            _writes.Release();
        }
    }

    private async Task StoreAsync(string urlHash, string? etag, string? lastModified, byte[] body, CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var existing = await context.HttpResponseCache.AsTracking()
            .FirstOrDefaultAsync(e => e.UrlHash == urlHash, ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        if (existing is null)
        {
            context.HttpResponseCache.Add(new HttpCacheEntry
            {
                UrlHash = urlHash,
                ETag = etag,
                LastModified = lastModified,
                Body = Compress(body),
                CreatedAt = now
            });
        }
        else
        {
            existing.ETag = etag;
            existing.LastModified = lastModified;
            if (!DecompressIfNeeded(existing.Body).AsSpan().SequenceEqual(body))
                existing.Body = Compress(body);
            existing.CreatedAt = now;
        }
        await context.SaveChangesAsync(ct);
    }

    public async Task TouchAsync(string urlHash, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        await context.HttpResponseCache.Where(entry => entry.UrlHash == urlHash && entry.CreatedAt < now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.CreatedAt, now), ct);
    }

    public async Task RemoveAsync(string urlHash, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await context.HttpResponseCache.Where(entry => entry.UrlHash == urlHash).ExecuteDeleteAsync(ct);
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
