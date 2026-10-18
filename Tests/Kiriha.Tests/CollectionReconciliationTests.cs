using Kiriha.Core.Domain.Collections;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Tests;

public class CollectionReconciliationTests
{
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

        var result = CollectionReconciliation.Reconcile(
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
}
