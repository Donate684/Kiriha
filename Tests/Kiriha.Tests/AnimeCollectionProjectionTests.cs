using Kiriha.Core.Abstractions.Services;
using Kiriha.Core;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Constants;
using System.Collections.Specialized;
using Kiriha.Infrastructure;
using Kiriha.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Tests;

public sealed class AnimeCollectionProjectionTests
{
    [Fact]
    public void Rebuild_ComputesStatusBucketsWithRewatchingInWatching()
    {
        using var projection = new AnimeCollectionProjection();

        projection.Rebuild(
        [
            Item(1, "Watching", UserAnimeStatus.Watching),
            Item(2, "Completed", UserAnimeStatus.Completed),
            Item(3, "Rewatching", UserAnimeStatus.Completed, isRewatching: true),
        ]);

        Assert.Equal(2, projection.Count(UserAnimeStatus.Watching, MediaKind.Anime));
        Assert.Equal(1, projection.Count(UserAnimeStatus.Completed, MediaKind.Anime));
    }

    [Fact]
    public void Query_UsesPrecomputedSearchAndNsfwFlagsInsideSelectedStatus()
    {
        using var projection = new AnimeCollectionProjection();

        projection.Rebuild(
        [
            Item(1, "Frieren", UserAnimeStatus.Watching, russianTitle: "Volshebnitsa"),
            Item(2, "Adult Frieren", UserAnimeStatus.Watching, rating: "rx"),
            Item(3, "Frieren Completed", UserAnimeStatus.Completed),
        ]);

        var sfw = projection.Query(UserAnimeStatus.Watching, "volshebnitsa", filterNsfw: false, sortBy: AppConstants.Sorting.Title, kind: MediaKind.Anime);
        var nsfw = projection.Query(UserAnimeStatus.Watching, "frieren", filterNsfw: true, sortBy: AppConstants.Sorting.Title, kind: MediaKind.Anime);

        Assert.Equal(new[] { 1 }, sfw.Select(x => x.Id));
        Assert.Equal(new[] { 2 }, nsfw.Select(x => x.Id));
    }

    [Fact]
    public void ItemPropertyChange_MovesItemBetweenStatusBuckets()
    {
        using var projection = new AnimeCollectionProjection();
        var item = Item(1, "Frieren", UserAnimeStatus.PlanToWatch);

        projection.Rebuild([item]);
        item.Status = UserAnimeStatus.Watching;

        Assert.Equal(1, projection.Count(UserAnimeStatus.Watching, MediaKind.Anime));
        Assert.Equal(0, projection.Count(UserAnimeStatus.PlanToWatch, MediaKind.Anime));
    }

    [Fact]
    public void RewatchingChange_MovesCompletedItemIntoWatchingBucket()
    {
        using var projection = new AnimeCollectionProjection();
        var item = Item(1, "Frieren", UserAnimeStatus.Completed);

        projection.Rebuild([item]);
        item.IsRewatching = true;

        Assert.Equal(1, projection.Count(UserAnimeStatus.Watching, MediaKind.Anime));
        Assert.Equal(0, projection.Count(UserAnimeStatus.Completed, MediaKind.Anime));
    }

    [Fact]
    public void ApplyCollectionChange_AddsAndRemovesIncrementally()
    {
        using var projection = new AnimeCollectionProjection();
        var added = Item(1, "Frieren", UserAnimeStatus.Watching);

        projection.ApplyCollectionChange(
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, added),
            [added]);

        projection.ApplyCollectionChange(
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, added),
            []);

        Assert.Equal(0, projection.Count(UserAnimeStatus.Watching, MediaKind.Anime));
    }

    [Fact]
    public void Query_WithPrioritizeNewEpisodes_PutsNewEpisodesFirst()
    {
        using var projection = new AnimeCollectionProjection();

        // Item 1: Title "A Title", no new episode
        var item1 = Item(1, "A Title", UserAnimeStatus.Watching);
        item1.Progress = 10;
        item1.EpisodesAired = 10;

        // Item 2: Title "B Title", has confirmed new episode (aired 12 > progress 10, last episode aired recently)
        var item2 = Item(2, "B Title", UserAnimeStatus.Watching);
        item2.Progress = 10;
        item2.EpisodesAired = 12;
        item2.LastEpisodeAt = DateTime.UtcNow.AddHours(-5);

        // Item 3: Title "C Title", has new episode badge via next episode overdue (New ep.?)
        var item3 = Item(3, "C Title", UserAnimeStatus.Watching);
        item3.Progress = 5;
        item3.EpisodesAired = 5;
        item3.NextEpisodeAt = DateTime.UtcNow.AddHours(-1);
        item3.StatusDetailed = "currently_airing";

        // Item 4: Title "Z Title", has confirmed new episode
        var item4 = Item(4, "Z Title", UserAnimeStatus.Watching);
        item4.Progress = 1;
        item4.EpisodesAired = 2;
        item4.LastEpisodeAt = DateTime.UtcNow.AddHours(-1);

        projection.Rebuild([item1, item2, item3, item4]);

        // When prioritizeNewEpisodes is false, sorting by Title is alphabetical: 1 (A), 2 (B), 3 (C), 4 (Z)
        var normalSort = projection.Query(UserAnimeStatus.Watching, null, false, AppConstants.Sorting.Title, MediaKind.Anime, prioritizeNewEpisodes: false);
        Assert.Equal(new[] { 1, 2, 3, 4 }, normalSort.Select(x => x.Id));

        // When prioritizeNewEpisodes is true, items with new episodes (2, 3, 4) come first (sorted by Title: B, C, Z), then item 1 (A)
        var prioritySort = projection.Query(UserAnimeStatus.Watching, null, false, AppConstants.Sorting.Title, MediaKind.Anime, prioritizeNewEpisodes: true);
        Assert.Equal(new[] { 2, 3, 4, 1 }, prioritySort.Select(x => x.Id));
    }

    private static AnimeEntity Item(
        int id,
        string title,
        UserAnimeStatus status,
        string? russianTitle = null,
        string? rating = null,
        bool isRewatching = false)
    {
        return new AnimeEntity
        {
            Id = id,
            Title = title,
            RussianTitle = russianTitle,
            Status = status,
            Rating = rating,
            IsRewatching = isRewatching,
        };
    }
}
