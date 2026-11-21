using System.Text.Json;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Library;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Data.Repository;
using Kiriha.Services.AppLifecycle;
using Kiriha.ViewModels.AnimeDetails;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kiriha.Tests;

public sealed partial class ScrobbleSyncRestartTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public async Task CardEditor_AfterScrobble_MergesIndependentChangesAndDetectsConflicts(
        int? editedProgress, bool editNotes)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenCardLibraryAsync(ct);
        var item = new AnimeEntity { Id = AnimeId, Title = "Test title", TotalEpisodes = 12, Notes = "Original note" };
        Assert.True((await scenario.Actions.AddToListAsync(item, UserAnimeStatus.Watching, 2, ct)).Success);

        var draft = item.Clone();
        using var editor = new AnimeEditViewModel(item, draft, scenario.Actions, scenario.Library);
        Assert.True(await scenario.Progress.RecordScrobbleAsync(item, 4, UserAnimeStatus.Watching, ct));
        Assert.False(editor.HasChanges);
        if (editedProgress.HasValue)
            draft.Progress = editedProgress.Value;
        if (editNotes)
        {
            draft.Notes = "My edited note";
            draft.DateStarted = null;
        }
        Assert.True(editor.HasChanges);

        bool conflict = editedProgress == 3;
        Assert.Equal(!conflict, await editor.TrySaveAsync());
        Assert.Equal(conflict, editor.HasFieldConflict);
        Assert.Equal(conflict, editor.HasChanges);
        Assert.Equal(editedProgress == 3 ? 3 : 4, draft.Progress);
        var history = await scenario.History.GetHistoryAsync(ct: ct);
        Assert.Equal(editNotes ? 3 : 2, history.Count);
        Assert.DoesNotContain(history, row => row.ActionType == (int)HistoryActionType.Reverted);
        var queue = await scenario.Queue.GetPendingAsync(ct);
        Assert.Equal(editNotes ? 3 : 2, queue.Count);

        var saved = await scenario.Store.GetByIdAsync(AnimeId, ct: ct);
        Assert.NotNull(saved);
        Assert.Equal(4, saved.Progress);
        Assert.Equal(editNotes ? "My edited note" : "Original note", saved.Notes);
        if (editNotes)
        {
            Assert.Null(saved.DateStarted);
            Assert.Contains(history, row => row.ActionType == (int)HistoryActionType.Edited);
            var latestTask = queue.MaxBy(row => row.Id);
            Assert.NotNull(latestTask?.Payload);
            var payload = JsonSerializer.Deserialize<AnimeEntity>(latestTask.Payload);
            Assert.NotNull(payload);
            Assert.Equal((saved.Progress, saved.Notes, saved.DateStarted),
                (payload.Progress, payload.Notes, payload.DateStarted));
        }
    }

    [Fact]
    public async Task MangaCard_AfterTrackerRefresh_KeepsNewChaptersAndMetadataWhileSavingVolumes()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenCardLibraryAsync(ct);
        var item = new AnimeEntity
        {
            Id = AnimeId, Title = "Manga", MediaKind = MediaKind.Manga, Status = UserAnimeStatus.Watching,
            ChaptersRead = 2, VolumesRead = 1
        };
        await scenario.Library.AddOrUpdateAnimeAsync(item);
        var draft = item.Clone();
        using var editor = new AnimeEditViewModel(item, draft, scenario.Actions, scenario.Library);
        draft.VolumesRead = 2;

        var remote = item.Clone();
        remote.ChaptersRead = 12;
        remote.Synopsis = "New synopsis from the tracker";
        scenario.Tracker.Setup(t => t.GetUserMangaListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([remote]);
        var refresh = new AnimeSyncOrchestrator(
            scenario.Library, scenario.Store, [scenario.Tracker.Object], Mock.Of<IRecognitionCache>(),
            syncGate: scenario.Gate, syncTaskRepo: scenario.Queue, eventBus: new CoreEventBus());
        Assert.True(await refresh.SyncMangaWithTrackersAsync(ct: ct));
        Assert.True(await editor.TrySaveAsync());

        var saved = await scenario.Store.GetByIdAsync(AnimeId, MediaKind.Manga, ct);
        Assert.NotNull(saved);
        Assert.Equal((12, 2, remote.Synopsis), (saved.ChaptersRead, saved.VolumesRead, saved.Synopsis));
        Assert.Equal(12, draft.ChaptersRead);
        Assert.False(editor.HasChanges);
        var edit = Assert.Single(await scenario.History.GetHistoryAsync(ct: ct));
        Assert.Equal((int)HistoryActionType.Edited, edit.ActionType);
    }

    [Fact]
    public async Task CardDetails_WhenMetadataArrivesAfterScrobble_OnlyUpdateCatalogFields()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenCardLibraryAsync(ct);
        var item = new AnimeEntity { Id = AnimeId, Title = "Test title", TotalEpisodes = 12, Notes = "Local notes" };
        var draft = item.Clone();
        using var editor = new AnimeEditViewModel(item, draft, scenario.Actions, scenario.Library);
        await editor.AddToListCommand.ExecuteAsync(null);
        Assert.True(editor.IsInList);
        Assert.False(editor.HasChanges);
        var canonical = scenario.Library.FindById(AnimeId);
        Assert.NotNull(canonical);
        Assert.True(await scenario.Progress.RecordScrobbleAsync(canonical, 4, null, ct));

        var details = new AnimeEntity
        {
            Id = AnimeId, Title = item.Title, Status = UserAnimeStatus.Completed, Progress = 12,
            Score = "10", Notes = "Outdated notes", Synopsis = "Full synopsis", Genres = ["Drama"]
        };
        Assert.Null(await scenario.Library.RefreshEditorDetailsAsync(item, details, editor.EntryGeneration, ct));
        var saved = await scenario.Store.GetByIdAsync(AnimeId, ct: ct);
        Assert.NotNull(saved);
        Assert.Equal((UserAnimeStatus.Watching, 4, "Local notes", "-"), (saved.Status, saved.Progress, saved.Notes, saved.Score));
        Assert.Equal(details.Synopsis, saved.Synopsis);
        Assert.Equal(details.Genres, saved.Genres);
        Assert.Equal(details.Synopsis, canonical.Synopsis);
        Assert.False(editor.HasChanges);
        Assert.Equal(2, (await scenario.History.GetHistoryAsync(ct: ct)).Count);
    }

    [Fact]
    public async Task CardEditor_AfterRemovalAndReAdd_CannotModifyReplacementOrRecreateDeletedEntry()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenCardLibraryAsync(ct);
        var item = new AnimeEntity { Id = AnimeId, Title = "Test title" };
        var remoteDetails = item.Clone();
        remoteDetails.Status = UserAnimeStatus.Watching;
        remoteDetails.Progress = 5;
        remoteDetails.Synopsis = "Delayed details";
        using var beforeAdd = new AnimeEditViewModel(item, item.Clone(), scenario.Actions, scenario.Library);
        var imported = await scenario.Library.RefreshEditorDetailsAsync(item, remoteDetails, beforeAdd.EntryGeneration, ct);
        Assert.NotNull(imported);
        Assert.Equal(5, imported.Value.Fields.Progress);
        Assert.Equal(imported.Value.Generation, scenario.Library.GetEntryGeneration(AnimeId, MediaKind.Anime));

        var draft = item.Clone();
        using var oldEditor = new AnimeEditViewModel(item, draft, scenario.Actions, scenario.Library);
        draft.Notes = "Old draft";
        Assert.True((await scenario.Actions.RemoveFromListAsync(AnimeId, ct)).Success);
        Assert.Null(await scenario.Library.RefreshEditorDetailsAsync(item, remoteDetails, oldEditor.EntryGeneration, ct));
        Assert.Null(await scenario.Store.GetByIdAsync(AnimeId, ct: ct));
        Assert.False(await oldEditor.TrySaveAsync());
        Assert.True(oldEditor.HasEntryConflict);

        // Reuse the original object to prove that membership is checked independently of its reference.
        item.Notes = "Replacement";
        item.Synopsis = string.Empty;
        Assert.True((await scenario.Actions.AddToListAsync(item, UserAnimeStatus.OnHold, 7, ct)).Success);
        Assert.Same(item, scenario.Library.FindById(AnimeId));
        await beforeAdd.AddToListCommand.ExecuteAsync(null);
        Assert.True(beforeAdd.HasEntryConflict);
        Assert.False(beforeAdd.IsInList);
        await oldEditor.RemoveFromList(new object());
        await oldEditor.RemoveFromList(new object());
        Assert.True(oldEditor.HasEntryConflict);
        Assert.False(await oldEditor.TrySaveAsync());
        Assert.Equal("Old draft", draft.Notes);
        Assert.Null(await scenario.Library.RefreshEditorDetailsAsync(item, remoteDetails, oldEditor.EntryGeneration, ct));

        var saved = await scenario.Store.GetByIdAsync(AnimeId, ct: ct);
        Assert.NotNull(saved);
        Assert.Equal((UserAnimeStatus.OnHold, 7, "Replacement", string.Empty),
            (saved.Status, saved.Progress, saved.Notes, saved.Synopsis));
        Assert.Equal(2, (await scenario.History.GetHistoryAsync(ct: ct)).Count);
        Assert.Equal(2, (await scenario.Queue.GetPendingAsync(ct)).Count);
    }

    private async Task<CardLibrary> OpenCardLibraryAsync(CancellationToken ct)
    {
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        await setup.Database.EnsureCreatedAsync(ct);
        var store = new UserAnimeRepository(factory);
        var queue = new SyncTaskRepository(factory);
        var history = new HistoryService(new HistoryRepository(factory));
        var initializer = Mock.Of<IDatabaseInitializer>(db => db.InitializationTask == Task.CompletedTask);
        var dispatcher = new ImmediateProgressDispatcher();
        var tracker = CreateTracker(Shikimori, _ => Task.FromResult(SyncOutcome.Success));
        var background = new BackgroundTaskSupervisor();
        var gate = new LibrarySyncGate();
        var library = new AnimeRepository(store, initializer, background, dispatcher, Mock.Of<IRecognitionCache>(), gate);
        var sync = CreateSyncManager(factory, queue, history, background, tracker.Object);
        var writer = new LibraryEntryWriter(library, sync, gate);
        var events = new CoreEventBus();
        var progress = new AnimeProgressService(library, store, sync, history, dispatcher,
            eventBus: events, syncGate: gate, entryWriter: writer);
        var actions = new AnimeListActionService(library, sync, progress, history, dispatcher, [tracker.Object],
            store, entryWriter: writer, eventBus: events);
        await library.InitializeAsync();
        return new CardLibrary(library, store, history, queue, actions, progress, tracker, gate, background, sync);
    }

    private sealed record CardLibrary(
        AnimeRepository Library, UserAnimeRepository Store, HistoryService History, SyncTaskRepository Queue,
        AnimeListActionService Actions, AnimeProgressService Progress, Mock<ITrackerService> Tracker,
        LibrarySyncGate Gate, BackgroundTaskSupervisor Background, SyncManager Sync) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Sync.StopAsync(CancellationToken.None);
            await History.FlushAsync(Timeout);
            await Background.StopAsync(CancellationToken.None);
            Progress.Dispose();
            Gate.Dispose();
            Background.Dispose();
        }
    }
}
