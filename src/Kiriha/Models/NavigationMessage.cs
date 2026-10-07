namespace Kiriha.Models;

public enum NavigationPage
{
    Home,
    AnimeList,
    Profile,
    Seasonal,
    History,
    Torrents,
    Search,
    Settings,
    Welcome
}

public sealed record NavigationMessage(NavigationPage Page);

