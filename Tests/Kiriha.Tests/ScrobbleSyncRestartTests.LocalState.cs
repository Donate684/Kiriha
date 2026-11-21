using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Mapping;
using Kiriha.Data.Repository;
using Kiriha.Mpv.UI.Services.Player;
using Kiriha.Services.AppLifecycle.Shutdown;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kiriha.Tests;

public sealed partial class ScrobbleSyncRestartTests
{
    private const string DeletedState = "removed episode";
    private const string LatestState = "latest episode";

    [Fact]
    public async Task LocalState_FailedWritesAndDeletesAreRetriedWhenShuttingDown()
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        await setup.Database.EnsureCreatedAsync();
        var resume = new SqlitePlaybackResumeStore(factory);
        var countdown = new SqliteScrobbleProgressStore(factory);
        var mapping = new ManualMappingService(new TitleMappingRepository(factory));

        resume.SavePosition(DeletedState, 24, 120);
        countdown.SaveElapsed(DeletedState, 6);
        mapping.AddMapping(DeletedState, AnimeId);
        await resume.FlushAsync();
        await countdown.FlushAsync();
        await mapping.FlushAsync();

        await setup.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER fail_resume_insert BEFORE INSERT ON playback_resume BEGIN SELECT RAISE(ABORT, 'test failure'); END;
            CREATE TRIGGER fail_resume_delete BEFORE DELETE ON playback_resume BEGIN SELECT RAISE(ABORT, 'test failure'); END;
            CREATE TRIGGER fail_timer_insert BEFORE INSERT ON scrobble_progress BEGIN SELECT RAISE(ABORT, 'test failure'); END;
            CREATE TRIGGER fail_timer_delete BEFORE DELETE ON scrobble_progress BEGIN SELECT RAISE(ABORT, 'test failure'); END;
            CREATE TRIGGER fail_mapping_insert BEFORE INSERT ON title_mappings BEGIN SELECT RAISE(ABORT, 'test failure'); END;
            CREATE TRIGGER fail_mapping_delete BEFORE DELETE ON title_mappings BEGIN SELECT RAISE(ABORT, 'test failure'); END;
            """);

        resume.SavePosition(LatestState, 10, 120);
        resume.Remove(LatestState);
        resume.SavePosition(LatestState, 30, 120);
        resume.Remove(DeletedState);
        resume.Remove(DeletedState);
        countdown.SaveElapsed(LatestState, 2);
        countdown.Clear(LatestState);
        countdown.SaveElapsed(LatestState, 9);
        countdown.Clear(DeletedState);
        countdown.Clear(DeletedState);
        mapping.AddMapping(LatestState, AnimeId + 1);
        mapping.RemoveMapping(LatestState);
        mapping.AddNegativeMapping(LatestState);
        Assert.True(mapping.RemoveMapping(DeletedState));
        Assert.False(mapping.RemoveMapping(DeletedState));

        await Assert.ThrowsAsync<IOException>(() => resume.FlushAsync());
        await Assert.ThrowsAsync<IOException>(() => countdown.FlushAsync());
        await Assert.ThrowsAsync<IOException>(() => mapping.FlushAsync());
        Assert.Equal(30, resume.TryGetResumePosition(LatestState));
        Assert.Equal(9, countdown.GetElapsed(LatestState));
        Assert.True(mapping.IsNegativelyMapped(LatestState));
        Assert.Null(resume.TryGetResumePosition(DeletedState));
        Assert.Equal(0, countdown.GetElapsed(DeletedState));
        Assert.False(mapping.TryGetMapping(DeletedState, out _));

        await setup.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER fail_resume_insert;
            DROP TRIGGER fail_resume_delete;
            DROP TRIGGER fail_timer_insert;
            DROP TRIGGER fail_timer_delete;
            DROP TRIGGER fail_mapping_insert;
            DROP TRIGGER fail_mapping_delete;
            """);
        await new ManualMappingShutdownHandler(mapping, countdown, resume).FlushAsync();

        var restartedFactory = CreateFactory();
        var restoredResume = new SqlitePlaybackResumeStore(restartedFactory);
        var restoredCountdown = new SqliteScrobbleProgressStore(restartedFactory);
        var restoredMapping = new ManualMappingService(new TitleMappingRepository(restartedFactory));
        Assert.Equal(30, restoredResume.TryGetResumePosition(LatestState));
        Assert.Equal(9, restoredCountdown.GetElapsed(LatestState));
        Assert.True(restoredMapping.IsNegativelyMapped(LatestState));
        Assert.Null(restoredResume.TryGetResumePosition(DeletedState));
        Assert.Equal(0, restoredCountdown.GetElapsed(DeletedState));
        Assert.False(restoredMapping.TryGetMapping(DeletedState, out _));
        Assert.False(restoredMapping.IsNegativelyMapped(DeletedState));
    }

    [Fact]
    public async Task LegacyLocalState_FailedImportsKeepSourceFilesAndCanRetryInSameProcess()
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var resumePath = Path.Combine(_directory.FullName, "resume.json");
        var countdownPath = Path.Combine(_directory.FullName, "timer.json");
        var mappingPath = Path.Combine(_directory.FullName, "mapping.json");
        await File.WriteAllTextAsync(resumePath, """{"legacy":{"position":42,"duration":100}}""");
        await File.WriteAllTextAsync(countdownPath, """{"legacy":16}""");
        await File.WriteAllTextAsync(mappingPath, """{"version":2,"mappings":{"legacy":314}}""");

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER fail_resume_import BEFORE INSERT ON playback_resume BEGIN SELECT RAISE(ABORT, 'test failure'); END;
            CREATE TRIGGER fail_timer_import BEFORE INSERT ON scrobble_progress BEGIN SELECT RAISE(ABORT, 'test failure'); END;
            CREATE TRIGGER fail_mapping_import BEFORE INSERT ON title_mappings BEGIN SELECT RAISE(ABORT, 'test failure'); END;
            """);

        var resume = new SqlitePlaybackResumeStore(factory, resumePath);
        var countdown = new SqliteScrobbleProgressStore(factory, countdownPath);
        var mapping = new ManualMappingService(new TitleMappingRepository(factory), mappingPath);
        _ = resume.TryGetResumePosition("legacy");
        _ = countdown.GetElapsed("legacy");
        _ = mapping.TryGetMapping("legacy", out _);
        Assert.True(File.Exists(resumePath));
        Assert.True(File.Exists(countdownPath));
        Assert.True(File.Exists(mappingPath));
        Assert.False(await db.PlaybackResume.AnyAsync());
        Assert.False(await db.ScrobbleProgress.AnyAsync());
        Assert.False(await db.TitleMappings.AnyAsync());

        await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER fail_resume_import;
            DROP TRIGGER fail_timer_import;
            DROP TRIGGER fail_mapping_import;
            """);
        Assert.Equal(42, resume.TryGetResumePosition("legacy"));
        Assert.Equal(16, countdown.GetElapsed("legacy"));
        Assert.True(mapping.TryGetMapping("legacy", out var id));
        Assert.Equal(314, id);
        Assert.False(File.Exists(resumePath));
        Assert.False(File.Exists(countdownPath));
        Assert.False(File.Exists(mappingPath));
        Assert.Equal(42, new SqlitePlaybackResumeStore(CreateFactory()).TryGetResumePosition("legacy"));
        Assert.Equal(16, new SqliteScrobbleProgressStore(CreateFactory()).GetElapsed("legacy"));
        Assert.True(new ManualMappingService(new TitleMappingRepository(CreateFactory())).TryGetMapping("legacy", out id));
        Assert.Equal(314, id);
    }

    [Fact]
    public async Task LocalState_CancelledFlushPreservesEditsQueuedDuringAnOlderWrite()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Mock<ITitleMappingRepository>(MockBehavior.Strict);
        repository.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        repository.Setup(repo => repo.UpsertAsync(It.IsAny<TitleMappingEntity>(), It.IsAny<CancellationToken>()))
            .Returns(async (TitleMappingEntity entry, CancellationToken _) =>
            {
                if (entry.TargetId == AnimeId)
                {
                    started.TrySetResult();
                    await release.Task.WaitAsync(Timeout);
                }
            });
        var service = new ManualMappingService(repository.Object);
        service.AddMapping(LatestState, AnimeId);
        try
        {
            await started.Task.WaitAsync(Timeout);
            service.AddNegativeMapping(LatestState);
            Assert.True(service.IsNegativelyMapped(LatestState));

            using var cancel = new CancellationTokenSource();
            var flush = service.FlushAsync(cancel.Token);
            await cancel.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flush);
        }
        finally
        {
            release.TrySetResult();
            await service.FlushAsync().WaitAsync(Timeout);
        }

        var writes = repository.Invocations
            .Where(call => call.Method.Name == nameof(ITitleMappingRepository.UpsertAsync))
            .Select(call => Assert.IsType<TitleMappingEntity>(call.Arguments[0]).TargetId)
            .ToArray();
        Assert.Equal([AnimeId, ManualMappingService.NegativeMappingId], writes);
    }

    [Fact]
    public async Task StandaloneResume_TwoInstancesMergeChangesAndKeepRemovals()
    {
        var path = Path.Combine(_directory.FullName, "resume.json");
        var first = new FilePlaybackResumeStore(path);
        var second = new FilePlaybackResumeStore(path);
        Assert.Null(first.TryGetResumePosition(DeletedState));
        Assert.Null(second.TryGetResumePosition(LatestState));

        await Task.WhenAll(
            Task.Run(() => first.SavePosition(DeletedState, 35, 120)),
            Task.Run(() => second.SavePosition(LatestState, 45, 120)));
        Assert.Equal(35, second.TryGetResumePosition(DeletedState));
        Assert.Equal(45, first.TryGetResumePosition(LatestState));

        second.Remove(DeletedState);
        first.SavePosition(LatestState, 55, 120);
        var reopened = new FilePlaybackResumeStore(path);
        Assert.Null(reopened.TryGetResumePosition(DeletedState));
        Assert.Equal(55, reopened.TryGetResumePosition(LatestState));
    }

    [Fact]
    public async Task StandaloneResume_FailedWriteKeepsPendingChangesUntilShutdown()
    {
        var path = Path.Combine(_directory.FullName, "resume.json");
        var store = new FilePlaybackResumeStore(path);
        store.SavePosition(DeletedState, 30, 120);
        var initial = await File.ReadAllTextAsync(path);
        await using (var lockedFile = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            store.Remove(DeletedState);
            store.SavePosition(LatestState, 50, 120);
            await Assert.ThrowsAsync<IOException>(() => store.FlushAsync());
            Assert.Null(store.TryGetResumePosition(DeletedState));
            Assert.Equal(50, store.TryGetResumePosition(LatestState));
        }

        Assert.Equal(initial, await File.ReadAllTextAsync(path));
        await new ManualMappingShutdownHandler(playbackResumeStore: store).FlushAsync();
        var reopened = new FilePlaybackResumeStore(path);
        Assert.Null(reopened.TryGetResumePosition(DeletedState));
        Assert.Equal(50, reopened.TryGetResumePosition(LatestState));
        Assert.Empty(_directory.GetFiles("*.tmp"));
    }

    [Fact]
    public async Task StandaloneResume_CorruptFileIsPreservedWhenRebuilding()
    {
        var path = Path.Combine(_directory.FullName, "resume.json");
        const string damaged = "{incomplete";
        await File.WriteAllTextAsync(path, damaged);
        var store = new FilePlaybackResumeStore(path);
        Assert.Null(store.TryGetResumePosition(LatestState));
        store.SavePosition(LatestState, 35, 120);

        Assert.Equal(35, new FilePlaybackResumeStore(path).TryGetResumePosition(LatestState));
        var backup = Assert.Single(_directory.GetFiles("*.invalid"));
        Assert.Equal(damaged, await File.ReadAllTextAsync(backup.FullName));
    }
}
