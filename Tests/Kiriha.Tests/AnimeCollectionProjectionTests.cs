using System.Collections.Specialized;
using Kiriha.Core;
using Kiriha.Core.Domain.Constants;
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

    [Fact]
    public void Query_WithPrioritizeUnwatched_PutsUnwatchedTitlesFirst()
    {
        using var projection = new AnimeCollectionProjection();

        // Item 1: Title "A Title", fully caught up (10/10)
        var item1 = Item(1, "A Title", UserAnimeStatus.Watching);
        item1.Progress = 10;
        item1.EpisodesAired = 10;

        // Item 2: Title "B Title", unwatched episodes (5/10)
        var item2 = Item(2, "B Title", UserAnimeStatus.Watching);
        item2.Progress = 5;
        item2.EpisodesAired = 10;

        // Item 3: Title "C Title", fully watched finished series (12/12)
        var item3 = Item(3, "C Title", UserAnimeStatus.Watching);
        item3.Progress = 12;
        item3.TotalEpisodes = 12;
        item3.StatusDetailed = "released";

        // Item 4: Title "Z Title", unwatched episodes in finished series (3/12)
        var item4 = Item(4, "Z Title", UserAnimeStatus.Watching);
        item4.Progress = 3;
        item4.TotalEpisodes = 12;
        item4.StatusDetailed = "released";

        // Item 5: Title "M Title", fresh episode badge
        var item5 = Item(5, "M Title", UserAnimeStatus.Watching);
        item5.Progress = 1;
        item5.EpisodesAired = 2;
        item5.LastEpisodeAt = DateTime.UtcNow.AddHours(-2);

        projection.Rebuild([item1, item2, item3, item4, item5]);

        // Default sort (prioritizeUnwatched: false, prioritizeNewEpisodes: false):
        // Alphabetical: 1 (A), 2 (B), 3 (C), 5 (M), 4 (Z)
        var normalSort = projection.Query(UserAnimeStatus.Watching, null, false, AppConstants.Sorting.Title, MediaKind.Anime, prioritizeNewEpisodes: false, prioritizeUnwatched: false);
        Assert.Equal(new[] { 1, 2, 3, 5, 4 }, normalSort.Select(x => x.Id));

        // PrioritizeUnwatched = true:
        // Unwatched items are 2, 4, 5. Alphabetical among unwatched: 2 (B), 5 (M), 4 (Z).
        // Followed by caught up items: 1 (A), 3 (C).
        var unwatchedSort = projection.Query(UserAnimeStatus.Watching, null, false, AppConstants.Sorting.Title, MediaKind.Anime, prioritizeNewEpisodes: false, prioritizeUnwatched: true);
        Assert.Equal(new[] { 2, 5, 4, 1, 3 }, unwatchedSort.Select(x => x.Id));

        // Both PrioritizeNewEpisodes = true AND PrioritizeUnwatched = true:
        // 1st tier: New episode badge (Item 5)
        // 2nd tier: Other unwatched items (Item 2, Item 4 - alphabetical B, Z)
        // 3rd tier: Caught up items (Item 1, Item 3 - alphabetical A, C)
        var bothSort = projection.Query(UserAnimeStatus.Watching, null, false, AppConstants.Sorting.Title, MediaKind.Anime, prioritizeNewEpisodes: true, prioritizeUnwatched: true);
        Assert.Equal(new[] { 5, 2, 4, 1, 3 }, bothSort.Select(x => x.Id));
    }

    [Fact]
    public void CountAllStatuses_ReturnsFilteredCountsAcrossStatuses()
    {
        using var projection = new AnimeCollectionProjection();

        var item1 = Item(1, "Movie 1", UserAnimeStatus.Watching);
        item1.Type = "movie";
        item1.Genres = ["Action"];

        var item2 = Item(2, "TV 1", UserAnimeStatus.Watching);
        item2.Type = "tv";
        item2.Genres = ["Action", "Comedy"];

        var item3 = Item(3, "Movie 2", UserAnimeStatus.Completed);
        item3.Type = "movie";
        item3.Genres = ["Comedy"];

        var item4 = Item(4, "Movie 3", UserAnimeStatus.Completed);
        item4.Type = "movie";
        item4.Genres = ["Action"];

        var item5 = Item(5, "Adult Movie", UserAnimeStatus.Completed, rating: "rx");
        item5.Type = "movie";
        item5.Genres = ["Action"];

        projection.Rebuild([item1, item2, item3, item4, item5]);

        // Unfiltered (SFW)
        var allCounts = projection.CountAllStatuses(null, null, null, filterNsfw: false, MediaKind.Anime);
        Assert.Equal(2, allCounts.Watching);
        Assert.Equal(2, allCounts.Completed); // item5 is NSFW, so 2 SFW completed

        // Filter by format: movie
        var movieCounts = projection.CountAllStatuses(null, null, ["movie"], filterNsfw: false, MediaKind.Anime);
        Assert.Equal(1, movieCounts.Watching); // item1
        Assert.Equal(2, movieCounts.Completed); // item3, item4
        Assert.Equal(0, movieCounts.OnHold);

        // Filter by genre: Action
        var actionCounts = projection.CountAllStatuses(null, ["action"], null, filterNsfw: false, MediaKind.Anime);
        Assert.Equal(2, actionCounts.Watching); // item1, item2
        Assert.Equal(1, actionCounts.Completed); // item4

        // Filter by both: movie + Action
        var movieActionCounts = projection.CountAllStatuses(null, ["action"], ["movie"], filterNsfw: false, MediaKind.Anime);
        Assert.Equal(1, movieActionCounts.Watching); // item1
        Assert.Equal(1, movieActionCounts.Completed); // item4

        // Filter NSFW
        var nsfwCounts = projection.CountAllStatuses(null, null, ["movie"], filterNsfw: true, MediaKind.Anime);
        Assert.Equal(0, nsfwCounts.Watching);
        Assert.Equal(1, nsfwCounts.Completed); // item5

        // CountQuery matches Query count exactly
        var queryItems = projection.Query(UserAnimeStatus.Completed, null, ["action"], ["movie"], filterNsfw: false, null, MediaKind.Anime);
        var queryCount = projection.CountQuery(UserAnimeStatus.Completed, null, ["action"], ["movie"], filterNsfw: false, MediaKind.Anime);
        Assert.Equal(queryItems.Count, queryCount);
        Assert.Equal(movieActionCounts.Completed, queryCount);
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
