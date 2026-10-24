using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.ViewModels.Torrents;
using Moq;
using Xunit;

namespace Kiriha.Tests;

public class TorrentsModePersistenceTests
{
    private static TorrentsViewModel CreateViewModel(AppSettings settings, Mock<ISettingsService> settingsMock)
    {
        settingsMock.SetupGet(s => s.Current).Returns(settings);

        var animeRepoMock = new Mock<IAnimeRepository>();
        animeRepoMock.SetupGet(r => r.Collection).Returns(new ObservableCollection<AnimeEntity>());

        var filterRepoMock = new Mock<ITorrentFilterRepository>();
        filterRepoMock.Setup(r => r.GetHiddenAnimeIds()).Returns(new HashSet<int>());

        var rssMock = new Mock<IRssFeedService>();
        var torrServerMock = new Mock<ITorrServerService>();
        var localizerMock = new Mock<ILocalizer>();

        return new TorrentsViewModel(
            rssMock.Object,
            animeRepoMock.Object,
            settingsMock.Object,
            filterRepoMock.Object,
            torrServerMock.Object,
            localizerMock.Object);
    }

    [Fact]
    public void IsOnlineMode_LoadsTrueFromSettings_WhenConfigured()
    {
        var settings = new AppSettings();
        settings.Torrents.IsOnlineMode = true;
        var settingsMock = new Mock<ISettingsService>();

        var vm = CreateViewModel(settings, settingsMock);

        Assert.True(vm.IsOnlineMode);
    }

    [Fact]
    public void IsOnlineMode_LoadsFalseFromSettings_ByDefault()
    {
        var settings = new AppSettings();
        settings.Torrents.IsOnlineMode = false;
        var settingsMock = new Mock<ISettingsService>();

        var vm = CreateViewModel(settings, settingsMock);

        Assert.False(vm.IsOnlineMode);
    }

    [Fact]
    public void ChangingIsOnlineMode_PersistsToSettingsService()
    {
        var settings = new AppSettings();
        settings.Torrents.IsOnlineMode = false;
        var settingsMock = new Mock<ISettingsService>();

        Action<AppSettings>? capturedUpdate = null;
        settingsMock
            .Setup(s => s.Update(It.IsAny<Action<AppSettings>>(), SettingsSection.Torrents, true))
            .Callback<Action<AppSettings>, SettingsSection, bool>((act, _, _) =>
            {
                capturedUpdate = act;
                act(settings);
            });

        var vm = CreateViewModel(settings, settingsMock);
        Assert.False(vm.IsOnlineMode);

        vm.IsOnlineMode = true;

        Assert.NotNull(capturedUpdate);
        Assert.True(settings.Torrents.IsOnlineMode);
    }

    [Fact]
    public void ToggleSearchMode_TogglesAndPersistsIsOnlineMode()
    {
        var settings = new AppSettings();
        settings.Torrents.IsOnlineMode = true;
        var settingsMock = new Mock<ISettingsService>();

        settingsMock
            .Setup(s => s.Update(It.IsAny<Action<AppSettings>>(), SettingsSection.Torrents, true))
            .Callback<Action<AppSettings>, SettingsSection, bool>((act, _, _) => act(settings));

        var vm = CreateViewModel(settings, settingsMock);
        Assert.True(vm.IsOnlineMode);

        vm.ToggleSearchMode();

        Assert.False(vm.IsOnlineMode);
        Assert.False(settings.Torrents.IsOnlineMode);
    }

    [Fact]
    public void DefaultOnlineSources_OnlyAniLibriaEnabledForNewUsers()
    {
        var settings = new AppSettings();
        var settingsMock = new Mock<ISettingsService>();

        var vm = CreateViewModel(settings, settingsMock);

        Assert.True(vm.FilterOnlineAniLibria);
        Assert.False(vm.FilterOnlineAnimeLib);
        Assert.False(vm.FilterOnlineDreamCast);
        Assert.False(vm.FilterOnlineYummyAnime);
        Assert.False(vm.FilterOnlineAnime365);
        Assert.Equal(1, vm.ActiveOnlineSourcesCount);

        var enabledProviders = vm.GetEnabledOnlineProviders();
        Assert.Single(enabledProviders);
        Assert.Equal("AniLibria", enabledProviders[0]);
    }

    [Fact]
    public void TorrentFilters_LoadsFromGlobalSettings_WhenNoTitleSelected()
    {
        var settings = new AppSettings();
        settings.Torrents.Filter1080p = true;
        settings.Torrents.FilterHevc = true;
        settings.Torrents.FilterEraiRaws = true;
        settings.Torrents.FilterOnlyNew = true;

        var settingsMock = new Mock<ISettingsService>();
        var vm = CreateViewModel(settings, settingsMock);

        Assert.True(vm.Filter1080p);
        Assert.True(vm.FilterHevc);
        Assert.True(vm.FilterEraiRaws);
        Assert.True(vm.FilterOnlyNew);
        Assert.False(vm.FilterVaryg);
        Assert.False(vm.FilterOnlyBatches);
    }

    [Fact]
    public void TorrentFilters_PersistsToGlobalSettings_WhenNoTitleSelected()
    {
        var settings = new AppSettings();
        var settingsMock = new Mock<ISettingsService>();
        settingsMock
            .Setup(s => s.Update(It.IsAny<Action<AppSettings>>(), SettingsSection.Torrents, true))
            .Callback<Action<AppSettings>, SettingsSection, bool>((act, _, _) => act(settings));

        var vm = CreateViewModel(settings, settingsMock);
        Assert.False(vm.Filter1080p);

        vm.Filter1080p = true;
        vm.FilterOnlyNew = true;

        Assert.True(settings.Torrents.Filter1080p);
        Assert.True(settings.Torrents.FilterOnlyNew);
    }

    [Fact]
    public void TorrentFilters_GlobalFormula_PersistsAndLoadsFromSettings()
    {
        var settings = new AppSettings();
        var settingsMock = new Mock<ISettingsService>();
        settingsMock
            .Setup(s => s.Update(It.IsAny<Action<AppSettings>>(), SettingsSection.Torrents, true))
            .Callback<Action<AppSettings>, SettingsSection, bool>((act, _, _) => act(settings));

        var vm = CreateViewModel(settings, settingsMock);
        Assert.False(vm.UseCustomQuery);

        vm.CustomQuery = "Global Custom Formula";
        vm.UseCustomQuery = true;

        Assert.True(settings.Torrents.UseCustomQuery);
        Assert.Equal("Global Custom Formula", settings.Torrents.CustomQuery);

        // Reload into a new VM
        var vm2 = CreateViewModel(settings, settingsMock);
        Assert.True(vm2.UseCustomQuery);
        Assert.Equal("Global Custom Formula", vm2.CustomQuery);
    }

    [Fact]
    public void TorrentFilters_PerTitle_PersistsAllCategoriesToFilterRepository()
    {
        var settings = new AppSettings();
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.SetupGet(s => s.Current).Returns(settings);

        var anime = new AnimeEntity { Id = 63337, Title = "FX Senshi Kurumi-chan" };
        var animeRepoMock = new Mock<IAnimeRepository>();
        animeRepoMock.SetupGet(r => r.Collection).Returns(new ObservableCollection<AnimeEntity> { anime });

        AppSettings.TorrentFilterSet? savedSet = null;
        var filterRepoMock = new Mock<ITorrentFilterRepository>();
        filterRepoMock.Setup(r => r.GetHiddenAnimeIds()).Returns(new HashSet<int>());
        filterRepoMock
            .Setup(r => r.TryGetCachedFilter(63337))
            .Returns(() => savedSet);
        filterRepoMock
            .Setup(r => r.SaveFilterAsync(63337, It.IsAny<AppSettings.TorrentFilterSet>(), It.IsAny<System.Threading.CancellationToken>()))
            .Callback<int, AppSettings.TorrentFilterSet, System.Threading.CancellationToken>((_, set, _) => savedSet = set)
            .Returns(Task.CompletedTask);

        var rssMock = new Mock<IRssFeedService>();
        var torrServerMock = new Mock<ITorrServerService>();
        var localizerMock = new Mock<ILocalizer>();

        var vm = new TorrentsViewModel(
            rssMock.Object,
            animeRepoMock.Object,
            settingsMock.Object,
            filterRepoMock.Object,
            torrServerMock.Object,
            localizerMock.Object);

        vm.SelectedAnime = anime;

        // Toggle Release groups, Quality, Source, Formula
        vm.FilterToonsHub = true;
        vm.Filter1080p = true;
        vm.FilterOnlyNew = true;
        vm.OnlyCrunchyroll = true;
        vm.CustomQuery = "FX Senshi Kurumi-chan ToonsHub";
        vm.UseCustomQuery = true;

        Assert.NotNull(savedSet);
        Assert.True(savedSet.FilterToonsHub);
        Assert.True(savedSet.Filter1080p);
        Assert.True(savedSet.FilterOnlyNew);
        Assert.True(savedSet.OnlyCrunchyroll);
        Assert.True(savedSet.UseCustomQuery);
        Assert.Equal("FX Senshi Kurumi-chan ToonsHub", savedSet.CustomQuery);
    }
}
