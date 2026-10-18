using Kiriha.Core.Domain.Extensions;

namespace Kiriha.Tests;

public class SafeIndexExtensionsTests
{
    [Fact]
    public void SafeIndexExtensions_ReturnsElementOrNull()
    {
        IReadOnlyList<string> playlist = ["Ep 1", "Ep 2", "Ep 3"];

        Assert.Equal("Ep 2", playlist[1, true]);
        Assert.Null(playlist[99, true]);
        Assert.Null(playlist[-1, true]);
    }
}
