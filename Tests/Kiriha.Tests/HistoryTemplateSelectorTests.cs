using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Kiriha.ViewModels.History;
using Kiriha.Views.History;
using Xunit;

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
}
