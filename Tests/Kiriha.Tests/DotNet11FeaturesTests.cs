using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data.Repository;
using Kiriha.Core.Domain.Extensions;
using Kiriha.Core;
using Xunit;

namespace Kiriha.Tests;

public class DotNet11FeaturesTests
{
    [Fact]
    public void Process_TryGetProcessById_WorksSafely()
    {
        using var current = Process.GetCurrentProcess();
        int currentPid = current.Id;

        // Valid PID
        bool found = Process.TryGetProcessById(currentPid, out var process);
        Assert.True(found);
        Assert.NotNull(process);
        using (process)
        {
            Assert.Equal(currentPid, process.Id);
        }

        // Non-existent PID should safely return false without throwing
        bool notFound = Process.TryGetProcessById(int.MaxValue - 1, out var deadProcess);
        Assert.False(notFound);
        Assert.Null(deadProcess);
    }

    [Fact]
    public void ReadOnlyMemoryStream_ReadsCorrectlyWithoutCopying()
    {
        byte[] original = [1, 2, 3, 4, 5];
        ReadOnlyMemory<byte> mem = original;

        using var stream = new ReadOnlyMemoryStream(mem);
        Assert.Equal(5, stream.Length);
        Assert.Equal(0, stream.Position);
        Assert.True(stream.CanRead);
        Assert.False(stream.CanWrite);

        byte[] buffer = new byte[3];
        int read = stream.Read(buffer, 0, 3);
        Assert.Equal(3, read);
        Assert.Equal(new byte[] { 1, 2, 3 }, buffer);
        Assert.Equal(3, stream.Position);
    }

    [Fact]
    public void AnimeFilterEngine_UsesEqualityComparerCreate()
    {
        List<AnimeEntity> list =
        [
            new() { Title = "Death Note" },
            new() { Title = "Attack on Titan" }
        ];

        var sorted = AnimeFilterEngine.SortInPlace(list, "Title", isSeasonal: true, prioritizeNewEpisodes: false);
        Assert.Equal("Attack on Titan", sorted[0].Title);
        Assert.Equal("Death Note", sorted[1].Title);
    }

    [Fact]
    public void UnionType_SyncOperationResult_ExhaustiveMatch()
    {
        SyncOperationResult r1 = new SyncSuccess(10);
        SyncOperationResult r2 = new SyncRateLimited(TimeSpan.FromSeconds(5));
        SyncOperationResult r3 = new SyncAuthExpired("AniList");
        SyncOperationResult r4 = new SyncNetworkError("Timeout");

        static string Describe(SyncOperationResult res) => res switch
        {
            SyncSuccess s => $"Success:{s.UpdatedCount}",
            SyncRateLimited rl => $"RateLimited:{rl.RetryAfter.TotalSeconds}s",
            SyncAuthExpired ae => $"AuthExpired:{ae.ServiceName}",
            SyncNetworkError ne => $"NetworkError:{ne.ErrorMessage}"
        };

        Assert.Equal("Success:10", Describe(r1));
        Assert.Equal("RateLimited:5s", Describe(r2));
        Assert.Equal("AuthExpired:AniList", Describe(r3));
        Assert.Equal("NetworkError:Timeout", Describe(r4));
    }

    [Fact]
    public void ClosedHierarchy_TrackingMessage_MatchesExhaustively()
    {
        TrackingMessage msg = new TrackingStatusMessage("Tracking");

        string desc = msg switch
        {
            MediaChangedMessage m => $"Media:{m.Media?.AnimeTitle}",
            AnimeMatchedMessage a => $"Anime:{a.Anime?.Title}",
            TrackingCountdownMessage c => $"Countdown:{c.Countdown}",
            TrackingStatusMessage s => $"Status:{s.Status}"
        };

        Assert.Equal("Status:Tracking", desc);
    }

    [Fact]
    public void CollectionExpressionArguments_CreatesConfiguredCollections()
    {
        HashSet<string> set = [with(StringComparer.OrdinalIgnoreCase), "Apple", "apple", "BANANA"];
        Assert.Equal(2, set.Count);
        Assert.Contains("apple", set);
        Assert.Contains("banana", set);

        List<int> list = [with(capacity: 50), 10, 20, 30];
        Assert.Equal(3, list.Count);
        Assert.True(list.Capacity >= 50);
    }

    [Fact]
    public void HttpCacheRepository_ZstandardCompression_RoundtripsSuccessfully()
    {
        string sampleText = "Kiriha desktop media player with .NET 11 and C# 15! " + new string('x', 200);
        byte[] raw = System.Text.Encoding.UTF8.GetBytes(sampleText);

        byte[] compressed = HttpCacheRepository.Compress(raw);
        Assert.True(compressed.Length < raw.Length);

        // Verify Zstandard magic number
        Assert.True(compressed.Length >= 4);
        Assert.Equal(0x28, compressed[0]);
        Assert.Equal(0xB5, compressed[1]);
        Assert.Equal(0x2F, compressed[2]);
        Assert.Equal(0xFD, compressed[3]);

        byte[] decompressed = HttpCacheRepository.DecompressIfNeeded(compressed);
        Assert.Equal(raw, decompressed);
    }

    [Fact]
    public void HttpCacheRepository_LegacyGZip_StillDecompressesCorrectly()
    {
        string sampleText = "Legacy GZip payload from earlier versions of Kiriha database cache! " + new string('z', 200);
        byte[] raw = System.Text.Encoding.UTF8.GetBytes(sampleText);

        byte[] gzipCompressed;
        using (var ms = new MemoryStream())
        {
            using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            {
                gz.Write(raw);
            }
            gzipCompressed = ms.ToArray();
        }

        // Verify GZip magic number
        Assert.Equal(0x1F, gzipCompressed[0]);
        Assert.Equal(0x8B, gzipCompressed[1]);

        byte[] decompressed = HttpCacheRepository.DecompressIfNeeded(gzipCompressed);
        Assert.Equal(raw, decompressed);
    }

    [Fact]
    public void CollectionReconciliation_FullJoin_Splits3WayCorrectly()
    {
        var localItems = new[]
        {
            new AnimeEntity { Id = 1, Title = "Local Only" },
            new AnimeEntity { Id = 2, Title = "Shared Anime" }
        };

        var remoteItems = new[]
        {
            new AnimeEntity { Id = 2, Title = "Shared Anime (Remote Update)" },
            new AnimeEntity { Id = 3, Title = "Remote Only" }
        };

        var result = Kiriha.Core.Domain.Collections.CollectionReconciliation.Reconcile(
            localItems,
            remoteItems,
            l => l.Id,
            r => r.Id);

        Assert.Single(result.LocalOnly);
        Assert.Equal(1, result.LocalOnly[0].Id);

        Assert.Single(result.RemoteOnly);
        Assert.Equal(3, result.RemoteOnly[0].Id);

        Assert.Single(result.Matched);
        Assert.Equal(2, result.Matched[0].Local.Id);
        Assert.Equal(2, result.Matched[0].Remote.Id);
    }

    [Fact]
    public void SafeIndexExtensions_ReturnsElementOrNull()
    {
        IReadOnlyList<string> playlist = ["Ep 1", "Ep 2", "Ep 3"];

        Assert.Equal("Ep 2", playlist[1, true]);
        Assert.Null(playlist[99, true]);
        Assert.Null(playlist[-1, true]);
    }
}
