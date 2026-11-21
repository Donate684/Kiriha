using System.Text.Json;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Sync;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kiriha.Tests;

public sealed partial class ScrobbleSyncRestartTests
{
    [Theory]
    [InlineData(MediaKind.Anime, false)]
    [InlineData(MediaKind.Anime, true)]
    [InlineData(MediaKind.Manga, true)]
    public async Task ManualLibraryEdits_RejectOldControlsAndPromptsAfterMembershipChanges(MediaKind kind, bool reAdd)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var item = new AnimeEntity { Id = AnimeId, Title = "Original", MediaKind = kind, Score = "4" };
        var sibling = new AnimeEntity
        {
            Id = AnimeId, Title = "Other media", MediaKind = kind == MediaKind.Anime ? MediaKind.Manga : MediaKind.Anime,
            Status = UserAnimeStatus.Watching, Progress = 2, ChaptersRead = 2, Score = "6"
        };
        await scenario.Library.AddOrUpdateAnimeAsync(sibling);
        Assert.True((await scenario.Actions.AddToListAsync(item, UserAnimeStatus.Completed, 7, ct)).Success);
        var original = scenario.Library.CaptureEntry(item);
        Assert.NotNull(original);
        var rating = new AnimeCompletedRatingPromptMessage(item) { Target = original };
        var rewatch = new AnimeRewatchPromptMessage(item, 1) { Target = original };

        Assert.True((await scenario.Actions.RemoveFromListAsync(AnimeId, kind, ct)).Success);
        if (reAdd)
        {
            item.Score = "8";
            Assert.True((await scenario.Actions.AddToListAsync(item, UserAnimeStatus.Completed, 3, ct)).Success);
        }

        var target = original.Value;
        Assert.False(await scenario.Progress.ChangeProgressAsync(target, 1));
        Assert.False(await scenario.Progress.ChangeProgressAsync(target, -1));
        Assert.False(await scenario.Progress.SetScoreAsync(target, rating.ExpectedScore, 10));
        Assert.False(await scenario.Progress.ConfirmRewatchAsync(target, rewatch.ExpectedRewatchCount, rewatch.Episode));
        var stored = await scenario.Store.GetByIdAsync(AnimeId, kind, ct);
        if (reAdd)
        {
            Assert.NotNull(stored);
            Assert.Equal((3, "8", 0), (ReadProgress(stored), stored.Score, stored.RewatchCount));
        }
        else
        {
            Assert.Null(stored);
        }

        var otherMedia = await scenario.Store.GetByIdAsync(AnimeId, sibling.MediaKind, ct);
        Assert.NotNull(otherMedia);
        Assert.Equal((2, "6"), (ReadProgress(otherMedia), otherMedia.Score));
        await AssertLibraryActionsAsync(scenario, reAdd ? 3 : 2, scrobbles: 0, ct);
    }

    [Fact]
    public async Task ManualProgress_AfterScrobble_OverlappingButtonsApplyToLatestValue()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var item = new AnimeEntity { Id = AnimeId, Title = "Ongoing", TotalEpisodes = 7 };
        Assert.True((await scenario.Actions.AddToListAsync(item, UserAnimeStatus.Watching, 2, ct)).Success);
        var target = new LibraryEntryTarget(new MediaKey(AnimeId, MediaKind.Anime), scenario.Library.GetEntryGeneration(AnimeId, MediaKind.Anime));
        Assert.Equal(ScrobbleOutcome.Saved, (await scenario.Progress.RecordScrobbleAsync(target.Key, target.Generation, 5, ct)).Outcome);

        var saves = await Task.WhenAll(
            scenario.Progress.ChangeProgressAsync(target, 1), scenario.Progress.ChangeProgressAsync(target, 1));
        Assert.All(saves, saved => Assert.True(saved));
        Assert.False(await scenario.Progress.ChangeProgressAsync(target, 1));
        Assert.True(await scenario.Progress.ChangeProgressAsync(target, -1));

        var stored = await scenario.Store.GetByIdAsync(AnimeId, ct: ct);
        Assert.NotNull(stored);
        Assert.Equal(6, stored.Progress);
        Assert.Equal(stored.Progress, item.Progress);
        var history = await scenario.History.GetHistoryAsync(ct: ct);
        Assert.Contains(history, row => row.Episode == 7 && row.ActionType == (int)HistoryActionType.Completed);
        var reverted = Assert.Single(history, row => row.ActionType == (int)HistoryActionType.Reverted);
        Assert.Equal(6, reverted.Episode);
        await AssertLibraryActionsAsync(scenario, actions: 5, scrobbles: 1, ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualRating_AfterTrackerRefresh_PreservesProgressAndRejectsChangedScore(bool changedScore)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var item = new AnimeEntity
        {
            Id = AnimeId, MediaKind = MediaKind.Manga, Title = "Manga", ChaptersRead = 2, VolumesRead = 1,
            Status = UserAnimeStatus.Watching, Score = "4"
        };
        await scenario.Library.AddOrUpdateAnimeAsync(item);
        var original = scenario.Library.CaptureEntry(item);
        Assert.NotNull(original);
        var rating = new AnimeCompletedRatingPromptMessage(item) { Target = original };
        var remote = item.Clone();
        remote.ChaptersRead = 10;
        remote.VolumesRead = 3;
        remote.Notes = "New tracker notes";
        remote.Score = changedScore ? "7" : "4";
        scenario.Tracker.Setup(tracker => tracker.GetUserMangaListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([remote]);
        var refresh = new AnimeSyncOrchestrator(scenario.Library, scenario.Store, [scenario.Tracker.Object],
            Mock.Of<IRecognitionCache>(), syncGate: scenario.Gate, syncTaskRepo: scenario.Queue, eventBus: scenario.Events);
        Assert.True(await refresh.SyncMangaWithTrackersAsync(ct: ct));

        var target = original.Value;
        Assert.Equal(!changedScore, await scenario.Progress.SetScoreAsync(target, rating.ExpectedScore, 9));
        Assert.False(await scenario.Progress.SetScoreAsync(target, rating.ExpectedScore, 9));
        var stored = await scenario.Store.GetByIdAsync(AnimeId, MediaKind.Manga, ct);
        Assert.NotNull(stored);
        Assert.Equal((10, 3, changedScore ? "7" : "9", remote.Notes),
            (stored.ChaptersRead, stored.VolumesRead, stored.Score, stored.Notes));

        await AssertLibraryActionsAsync(scenario, changedScore ? 0 : 1, scrobbles: 0, ct);
        if (changedScore) return;

        var task = Assert.Single(await scenario.Queue.GetPendingAsync(ct));
        Assert.NotNull(task.Payload);
        var synced = JsonSerializer.Deserialize<AnimeEntity>(task.Payload);
        Assert.NotNull(synced);
        Assert.Equal((10, 3, "9", remote.Notes), (synced.ChaptersRead, synced.VolumesRead, synced.Score, synced.Notes));
        Assert.True(await scenario.Progress.SetScoreAsync(target, "9", 0));
        Assert.False(await scenario.Progress.SetScoreAsync(target, "-", 0));
        Assert.Equal("-", item.Score);
        await AssertLibraryActionsAsync(scenario, actions: 2, scrobbles: 0, ct);
    }

    [Fact]
    public async Task ManualRewatch_RejectsPromptAfterStatusChangeOrAlreadyCompletedRewatchCycle()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var item = new AnimeEntity { Id = AnimeId, Title = "Short series", TotalEpisodes = 2 };
        Assert.True((await scenario.Actions.AddToListAsync(item, UserAnimeStatus.Completed, 0, ct)).Success);
        var original = scenario.Library.CaptureEntry(item);
        Assert.NotNull(original);
        var target = original.Value;
        var prompt = new AnimeRewatchPromptMessage(item, 1) { Target = target };

        var baseline = AnimeEditFields.From(item);
        var proposed = baseline with { Status = UserAnimeStatus.OnHold };
        Assert.True((await scenario.Actions.SaveEditorChangesAsync(AnimeId, MediaKind.Anime, target.Generation, baseline, proposed, ct)).Success);
        Assert.False(await scenario.Progress.ConfirmRewatchAsync(target, prompt.ExpectedRewatchCount, prompt.Episode));
        baseline = AnimeEditFields.From(item);
        proposed = baseline with { Status = UserAnimeStatus.Completed };
        Assert.True((await scenario.Actions.SaveEditorChangesAsync(AnimeId, MediaKind.Anime, target.Generation, baseline, proposed, ct)).Success);

        Assert.True(await scenario.Progress.ConfirmRewatchAsync(target, prompt.ExpectedRewatchCount, prompt.Episode));
        Assert.False(await scenario.Progress.ConfirmRewatchAsync(target, prompt.ExpectedRewatchCount, prompt.Episode));
        Assert.Equal((UserAnimeStatus.Watching, 1, 1, true), (item.Status, item.Progress, item.RewatchCount, item.IsRewatching));
        Assert.True(await scenario.Progress.ChangeProgressAsync(target, 1));
        Assert.False(await scenario.Progress.ConfirmRewatchAsync(target, prompt.ExpectedRewatchCount, prompt.Episode));
        var stored = await scenario.Store.GetByIdAsync(AnimeId, ct: ct);
        Assert.NotNull(stored);
        Assert.Equal((UserAnimeStatus.Completed, 2, 1, false),
            (stored.Status, stored.Progress, stored.RewatchCount, stored.IsRewatching));
        var history = await scenario.History.GetHistoryAsync(ct: ct);
        Assert.Single(history, row => row.ActionType == (int)HistoryActionType.Rewatching);
        await AssertLibraryActionsAsync(scenario, actions: 5, scrobbles: 0, ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualScoreOrRewatch_QueueFailureRollsBackAndCanRetry(bool rewatch)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var item = new AnimeEntity { Id = AnimeId, Title = "Completed series", TotalEpisodes = 12, Score = "4" };
        Assert.True((await scenario.Actions.AddToListAsync(item, UserAnimeStatus.Completed, 0, ct)).Success);
        var target = new LibraryEntryTarget(new MediaKey(AnimeId, MediaKind.Anime), scenario.Library.GetEntryGeneration(AnimeId, MediaKind.Anime));
        await using var context = CreateFactory().CreateDbContext();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER fail_quick_edit_queue BEFORE INSERT ON sync_tasks
            BEGIN
                SELECT RAISE(ABORT, 'Quick edit queue is unavailable');
            END;
            """, ct);

        await Assert.ThrowsAsync<DbUpdateException>(SaveAsync);
        var stored = await scenario.Store.GetByIdAsync(AnimeId, ct: ct);
        Assert.NotNull(stored);
        Assert.Equal((UserAnimeStatus.Completed, 12, 0, "4"), (item.Status, item.Progress, item.RewatchCount, item.Score));
        Assert.Equal(AnimeEditFields.From(item), AnimeEditFields.From(stored));
        await AssertLibraryActionsAsync(scenario, actions: 1, scrobbles: 0, ct);

        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_quick_edit_queue", ct);
        Assert.True(await SaveAsync());
        Assert.False(await SaveAsync());
        await AssertLibraryActionsAsync(scenario, actions: 2, scrobbles: 0, ct);

        Task<bool> SaveAsync() => rewatch
            ? scenario.Progress.ConfirmRewatchAsync(target, expectedRewatchCount: 0, episode: 1)
            : scenario.Progress.SetScoreAsync(target, "4", 8);
    }
}
