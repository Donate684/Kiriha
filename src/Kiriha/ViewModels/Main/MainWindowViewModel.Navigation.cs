using Kiriha.Core.Navigation;
using CommunityToolkit.Mvvm.Input;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Utils.Async;
using Kiriha.ViewModels.Search;
using Kiriha.ViewModels.Startup;
using Kiriha.Models;
using Kiriha.Core.Abstractions.Models;

namespace Kiriha.ViewModels.Main;

public partial class MainWindowViewModel
{
    public void Receive(NavigationMessage message)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            switch (message.Page)
            {
                case NavigationPage.Home: NavigateHome(); break;
                case NavigationPage.AnimeList: NavigateAnimeList(); break;
                case NavigationPage.Profile: NavigateAnalytics(); break;
                case NavigationPage.Seasonal: _ = NavigateSeasonal(); break;
                case NavigationPage.History: NavigateHistory(); break;
                case NavigationPage.Torrents: NavigateTorrents(); break;
                case NavigationPage.Search: NavigateSearch(); break;
                case NavigationPage.Settings: NavigateSettings(); break;
                case NavigationPage.Welcome: NavigateWelcome(); break;
            }
        });
    }

    partial void OnSelectedNavigationIndexChanged(int value)
    {
        if (IsNavigationBlocked)
        {
            // Revert selection if blocked
            return;
        }

        if (value >= 0) IsSettingsSelected = false;

        switch (value)
        {
            case 0: NavigateAnalytics(); break;
            case 1: NavigateHome(); break;
            case 2: NavigateAnimeList(); break;
            case 3: _ = NavigateSeasonal(); break;
            case 4: NavigateHistory(); break;
            case 5: NavigateTorrents(); break;
            case 6: NavigateSearch(); break;
        }
    }

    [RelayCommand]
    public void NavigateWelcome()
    {
        SelectedNavigationIndex = -1;
        IsSettingsSelected = false;
        SetCurrentPage(_viewModelFactory.Create<WelcomeViewModel>());
    }

    [RelayCommand]
    public void NavigateHome()
    {
        SetCurrentPage(EnsureNowPlayingViewModel());
    }

    [RelayCommand]
    public void NavigateAnimeList()
    {
        var animeList = EnsureAnimeListViewModel();
        animeList.RefreshLocalization();
        SetCurrentPage(animeList);
    }

    [RelayCommand]
    public async Task NavigateSeasonal()
    {
        var animeList = EnsureAnimeListViewModel();
        var seasonal = EnsureSeasonalViewModel();

        var itemsSnapshot = animeList.AnimeItems.ToArray();
        var userStore = await Task.Run(() => itemsSnapshot
            .GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First().Status));

        seasonal.UpdateUserList(userStore);
        // Trigger the initial Shikimori/MAL load on the very first navigation
        // (no-op on subsequent navigations - the call is idempotent). This
        // replaces an eager preload in SeasonalViewModel's ctor, which used
        // to fire HTTP requests during the app's first render frames.
        seasonal.EnsureInitialLoad();
        SetCurrentPage(seasonal);
    }

    [RelayCommand]
    public void NavigateHistory()
    {
        var history = EnsureHistoryViewModel();
        history.RefreshHistory().SafeFireAndForget("NavigateHistory");
        SetCurrentPage(history);
    }

    [RelayCommand]
    public void NavigateTorrents()
    {
        var torrents = EnsureTorrentsViewModel();
        torrents.RefreshWatchingList();
        SetCurrentPage(torrents);
    }

    [RelayCommand]
    public void NavigateSearch()
    {
        SetCurrentPage(_viewModelFactory.Create<SearchViewModel>());
    }

    [RelayCommand]
    public void NavigateAnalytics()
    {
        if (IsNavigationBlocked) return;
        SelectedNavigationIndex = 0;
        IsSettingsSelected = false;
        IsSettingsOpen = false;
        var analytics = EnsureAnalyticsViewModel();
        analytics.Refresh().SafeFireAndForget("NavigateAnalytics");
        SetCurrentPage(analytics);
    }
}
