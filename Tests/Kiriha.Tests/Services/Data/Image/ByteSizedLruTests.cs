using Kiriha.Services.Data.Image;
using Xunit;

namespace Kiriha.Tests.Services.Data.Image;

public class ByteSizedLruTests
{
    private sealed class TestItem
    {
        public string Value { get; }
        public long Size { get; set; }

        public TestItem(string value, long size)
        {
            Value = value;
            Size = size;
        }
    }

    [Fact]
    public void TryGet_NonExistentKey_ReturnsFalse()
    {
        var lru = new ByteSizedLru<string, TestItem>(100, item => item.Size);
        var found = lru.TryGet("missing", out var item);

        Assert.False(found);
        Assert.Null(item);
    }

    [Fact]
    public void Set_And_TryGet_ReturnsStoredValue()
    {
        var lru = new ByteSizedLru<string, TestItem>(100, item => item.Size);
        var item = new TestItem("alpha", 30);

        lru.Set("k1", item);
        var found = lru.TryGet("k1", out var retrieved);

        Assert.True(found);
        Assert.Same(item, retrieved);
    }

    [Fact]
    public void Set_OverBudgetSingleItem_DoesNotInsert()
    {
        var lru = new ByteSizedLru<string, TestItem>(100, item => item.Size);
        var hugeItem = new TestItem("huge", 150);

        lru.Set("k1", hugeItem);
        var found = lru.TryGet("k1", out var retrieved);

        Assert.False(found);
        Assert.Null(retrieved);
    }

    [Fact]
    public void Set_ExceedsBudget_EvictsLeastRecentlyUsed()
    {
        // Budget 100 bytes. Items are 40 bytes each. Max 2 items.
        var lru = new ByteSizedLru<string, TestItem>(100, item => item.Size);
        var item1 = new TestItem("first", 40);
        var item2 = new TestItem("second", 40);
        var item3 = new TestItem("third", 40);

        lru.Set("k1", item1);
        lru.Set("k2", item2);

        // Access k1 so k2 becomes least recently used
        Assert.True(lru.TryGet("k1", out _));

        // Adding k3 (40 bytes) pushes total to 120 -> evicts k2
        lru.Set("k3", item3);

        Assert.True(lru.TryGet("k1", out _));
        Assert.False(lru.TryGet("k2", out _));
        Assert.True(lru.TryGet("k3", out _));
    }

    [Fact]
    public void Set_UpdateExistingKey_ReusesNodeAndUpdatesSize()
    {
        var lru = new ByteSizedLru<string, TestItem>(100, item => item.Size);
        var item1 = new TestItem("initial", 40);
        var item2 = new TestItem("updated", 50);

        lru.Set("k1", item1);
        lru.Set("k1", item2);

        Assert.True(lru.TryGet("k1", out var retrieved));
        Assert.Same(item2, retrieved);

        // Adding another 40 byte item should fit within budget 100 (50 + 40 = 90 <= 100)
        var item3 = new TestItem("sibling", 40);
        lru.Set("k2", item3);

        Assert.True(lru.TryGet("k1", out _));
        Assert.True(lru.TryGet("k2", out _));
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var lru = new ByteSizedLru<string, TestItem>(100, item => item.Size);
        lru.Set("k1", new TestItem("one", 20));
        lru.Set("k2", new TestItem("two", 20));

        lru.Clear();

        Assert.False(lru.TryGet("k1", out _));
        Assert.False(lru.TryGet("k2", out _));
    }
}
