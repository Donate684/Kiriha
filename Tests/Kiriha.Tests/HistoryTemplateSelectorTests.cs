using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Kiriha.ViewModels.History;
using Kiriha.Views.History;

namespace Kiriha.Tests;

public class HistoryTemplateSelectorTests
{
    [Fact]
    public void HistoryItemTemplateSelector_RecyclesCorrectlyByType()
    {
        var selector = new HistoryItemTemplateSelector
        {
            HeaderTemplate = new FuncDataTemplate<HistoryDateHeaderItem>((h, _) => new TextBlock { Tag = "HEADER" }),
            EntryTemplate = new FuncDataTemplate<HistoryEntryVm>((e, _) => new Border { Tag = "ENTRY" })
        };

        var entry1 = new HistoryEntryVm(null!) { AnimeTitle = "Card 1" };
        var getArgs1 = new ElementFactoryGetArgs { Data = entry1 };
        var card1 = selector.GetElement(getArgs1);
        Assert.Equal("ENTRY", card1.Tag);
        Assert.Same(entry1, card1.DataContext);

        // Recycle card1
        selector.RecycleElement(new ElementFactoryRecycleArgs { Element = card1 });

        // Request a header -> MUST NOT reuse card1!
        var header = new HistoryDateHeaderItem { Header = "Today" };
        var getArgs2 = new ElementFactoryGetArgs { Data = header };
        var headerCtrl = selector.GetElement(getArgs2);
        Assert.Equal("HEADER", headerCtrl.Tag);
        Assert.NotSame(card1, headerCtrl);
        Assert.Same(header, headerCtrl.DataContext);

        // Request card 2 -> should reuse card1 and rebind DataContext
        var entry2 = new HistoryEntryVm(null!) { AnimeTitle = "Card 2" };
        var getArgs3 = new ElementFactoryGetArgs { Data = entry2 };
        var card2 = selector.GetElement(getArgs3);
        Assert.Equal("ENTRY", card2.Tag);
        Assert.Same(card1, card2);
        Assert.Same(entry2, card2.DataContext);

        // Also test Build(entry3)
        var entry3 = new HistoryEntryVm(null!) { AnimeTitle = "Card 3" };
        var card3 = selector.Build(entry3);
        Assert.NotNull(card3);
        Assert.Same(entry3, card3.DataContext);

        // Verify ItemsRepeater uses selector as its native template shim
        var repeater = new ItemsRepeater
        {
            ItemTemplate = selector
        };
        Assert.Same(selector, repeater.ItemTemplate);

        var shimProp = typeof(ItemsRepeater).GetProperty("ItemTemplateShim", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var shim = shimProp.GetValue(repeater);
        Assert.Same(selector, shim);
    }

    [Fact]
    public async Task HistoryEntryVm_CreatesInteractiveTrackerBadges_EvenWhenStatusEmpty()
    {
        var localizer = new Moq.Mock<Kiriha.Core.Abstractions.Services.ILocalizer>();
        localizer.Setup(l => l.GetLoc(Moq.It.IsAny<string>(), Moq.It.IsAny<object[]>()))
            .Returns<string, object[]>((k, a) => $"{k}:{string.Join(',', a)}");

        var entry = new HistoryEntryVm(localizer.Object)
        {
            AnimeId = 42,
            ActionType = 1, // Watched
            EpisodeFrom = 4,
            EpisodeTo = 4
        };

        Assert.True(entry.CanTrack);

        bool syncTriggered = false;
        string? syncedTracker = null;

        entry.LoadTrackerStatuses(
            new Dictionary<string, Kiriha.Core.Domain.Models.TrackerSyncInfo>(),
            tracker =>
            {
                syncTriggered = true;
                syncedTracker = tracker;
                return Task.CompletedTask;
            },
            tracker => true);

        Assert.True(entry.HasTrackerBadges);
        Assert.Equal(2, entry.TrackerBadges.Count);

        var shikiBadge = entry.TrackerBadges.First(b => b.TrackerName == "Shikimori");
        var malBadge = entry.TrackerBadges.First(b => b.TrackerName == "MyAnimeList");

        Assert.Equal("Shiki", shikiBadge.DisplayName);
        Assert.Equal(Kiriha.Core.Domain.Models.TrackerSyncState.NotSynced, shikiBadge.State);
        Assert.True(shikiBadge.IsClickable);

        Assert.Equal("MAL", malBadge.DisplayName);
        Assert.Equal(Kiriha.Core.Domain.Models.TrackerSyncState.NotSynced, malBadge.State);

        // Click Shiki button
        await shikiBadge.SyncCommand.ExecuteAsync(null);

        Assert.True(syncTriggered);
        Assert.Equal("Shikimori", syncedTracker);
        Assert.Equal(Kiriha.Core.Domain.Models.TrackerSyncState.Pending, shikiBadge.State);
        Assert.False(shikiBadge.IsClickable);

        // Update when server responds with Success
        entry.UpdateTrackerStatus("Shikimori", Kiriha.Core.Domain.Models.TrackerSyncState.Success, null);
        Assert.Equal(Kiriha.Core.Domain.Models.TrackerSyncState.Success, shikiBadge.State);
        Assert.Equal("CheckCircle", shikiBadge.IconKind);
        Assert.False(shikiBadge.IsClickable);
        Assert.Equal("Arrow", shikiBadge.CursorKind);
    }
}
