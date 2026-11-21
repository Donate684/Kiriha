using System.Net;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Auth;
using Kiriha.ViewModels.NowPlaying;
using Moq;

namespace Kiriha.Tests;

public sealed partial class CacheReliabilityTests
{
    [Fact]
    public async Task NowPlaying_ReusesCachedTitleThenRequestsNewTitleOnceDuringSameSession()
    {
        var (_, repo, clock) = await CreateCacheAsync();
        const int knownId = 42;
        const int newId = 43;
        var requestedIds = new List<int>();
        using var http = new HttpClient(new ScriptedHandler((request, _) =>
        {
            var id = request.RequestUri?.AbsolutePath.EndsWith("/42", StringComparison.Ordinal) == true ? knownId : newId;
            requestedIds.Add(id);
            return Json($$"""
                {"id":{{id}},"title":"Title {{id}}","status":"currently_airing",
                 "num_episodes":12,"genres":[{"id":1,"name":"Drama"}],"synopsis":"Catalog details"}
                """);
        }));
        var settings = Settings();
        using var tokens = new MalTokenManager(settings, new MalAuthService(http));
        using var api = new MalApiService(http, settings, tokens, repo, clock);
        Assert.NotNull(await api.GetAnimeDetailsAsync(knownId));
        Assert.Equal(new[] { knownId }, requestedIds);

        using var vm = new NowPlayingViewModel(Mock.Of<ITrackingService>(), settings, Mock.Of<IAnimeRepository>(),
            Mock.Of<IAnimeListActionService>(), Mock.Of<IShikiMetadataService>(), api, ILocalizer.Passthrough);

        var complete = new AnimeEntity
        {
            Id = 44, Title = "From library", TotalEpisodes = 12, Genres = ["Drama"], StatusDetailed = "currently_airing"
        };
        PublishMatch(vm, complete, 1);
        Assert.False(vm.IsLoadingDetails);
        Assert.Equal(new[] { knownId }, requestedIds);

        var existing = new AnimeEntity { Id = knownId, Title = "Shallow cached title" };
        await PlayAndWaitAsync(vm, existing, 1);
        Assert.Equal("Catalog details", existing.Synopsis);
        Assert.Equal(new[] { knownId }, requestedIds);

        var firstSeen = new AnimeEntity { Id = newId, Title = "New title" };
        await PlayAndWaitAsync(vm, firstSeen, 1);
        Assert.Equal(12, firstSeen.TotalEpisodes);
        Assert.Equal(new[] { knownId, newId }, requestedIds);

        var nextEpisode = new AnimeEntity { Id = newId, Title = "Same title, next episode" };
        await PlayAndWaitAsync(vm, nextEpisode, 2);
        Assert.Equal("Catalog details", nextEpisode.Synopsis);
        Assert.Equal(new[] { knownId, newId }, requestedIds);
    }

    [Fact]
    public async Task NowPlaying_ReopensSameEntryWithoutApplyingOldRefresh()
    {
        var first = new AnimeEntity
        {
            Id = 42, Title = "First", TotalEpisodes = 12, Genres = ["Drama"], StatusDetailed = "currently_airing"
        };
        var other = new AnimeEntity
        {
            Id = 43, Title = "Other", TotalEpisodes = 12, Genres = ["Drama"], StatusDetailed = "currently_airing"
        };
        var stale = new AnimeEntity { Id = first.Id, Title = first.Title, MeanScore = "8.00" };
        var oldRefresh = new TaskCompletionSource<AnimeEntity?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newRefresh = new TaskCompletionSource<AnimeEntity?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mal = new Mock<IMalApiService>();
        mal.Setup(x => x.ReadCachedDetailsAsync(first.Id, MediaKind.Anime, It.IsAny<CancellationToken>()))
            .ReturnsAsync((stale, false));
        mal.SetupSequence(x => x.RefreshCachedDetailsAsync(first.Id, MediaKind.Anime, It.IsAny<CancellationToken>()))
            .Returns(oldRefresh.Task).Returns(newRefresh.Task);
        using var vm = new NowPlayingViewModel(Mock.Of<ITrackingService>(), Settings(), Mock.Of<IAnimeRepository>(),
            Mock.Of<IAnimeListActionService>(), Mock.Of<IShikiMetadataService>(), mal.Object, ILocalizer.Passthrough);

        try
        {
            PublishMatch(vm, first, 1);
            first.TotalEpisodes = 0;
            var old = vm.EnsureFullDetailsSafeAsync(first);
            Assert.Equal("8.00", first.MeanScore);
            Assert.False(old.IsCompleted);

            PublishMatch(vm, other, 1);
            var reopened = PlayAndWaitAsync(vm, first, 2);
            oldRefresh.SetResult(new AnimeEntity { Id = first.Id, Title = first.Title, MeanScore = "1.00" });
            await old.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("8.00", first.MeanScore);
            Assert.True(vm.IsLoadingDetails);

            newRefresh.SetResult(new AnimeEntity { Id = first.Id, Title = first.Title, MeanScore = "9.00" });
            await reopened;
            Assert.Equal("9.00", first.MeanScore);
        }
        finally
        {
            oldRefresh.TrySetResult(null);
            newRefresh.TrySetResult(null);
        }
    }

    private static async Task PlayAndWaitAsync(NowPlayingViewModel vm, AnimeEntity title, int episode)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(NowPlayingViewModel.IsLoadingDetails) && !vm.IsLoadingDetails)
                finished.TrySetResult();
        }

        vm.PropertyChanged += OnChanged;
        try
        {
            PublishMatch(vm, title, episode);
            if (vm.IsLoadingDetails) await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            vm.PropertyChanged -= OnChanged;
        }
    }

    private static void PublishMatch(NowPlayingViewModel vm, AnimeEntity title, int episode)
    {
        var media = new ParsedMedia { OriginalTitle = $"{title.Title}-{episode}.mkv", AnimeTitle = title.Title, Episode = episode.ToString() };
        vm.Receive(new MediaChangedMessage(media));
        vm.Receive(new AnimeMatchedMessage(null));
        vm.Receive(new AnimeMatchedMessage(title));
    }
}
