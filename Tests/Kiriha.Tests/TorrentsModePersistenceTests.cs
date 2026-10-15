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
}
