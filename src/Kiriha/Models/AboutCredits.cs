using System.Collections.Generic;

namespace Kiriha.Models;

public static class AboutCredits
{
    public static IReadOnlyList<CreditEntry> DataSources { get; } =
    [
        new CreditEntry("MyAnimeList", "Account, list, scoring · myanimelist.net",  "https://myanimelist.net"),
        new CreditEntry("AniList",     "Episode airing schedule · anilist.co",      "https://anilist.co"),
        new CreditEntry("Shikimori",   "Russian titles & descriptions · shikimori.one", "https://shikimori.one"),
        new CreditEntry("ShikimoriRIP", "Community fork Shikimori · shikimori.rip", "https://shikimori.rip"),
        new CreditEntry("Nyaa.si",     "Torrent feed · nyaa.si",                    "https://nyaa.si"),
        new CreditEntry("Jikan API",   "MAL API wrapper · jikan.moe",               "https://jikan.moe"),
    ];

    public static IReadOnlyList<CreditEntry> Inspirations { get; } =
    [
        new CreditEntry("MAL Updater",       "Inspiration · malupdater.com", "https://malupdater.com/"),
        new CreditEntry("Taiga",             "Inspiration · erengy/taiga",   "https://github.com/erengy/taiga"),
    ];

    public static IReadOnlyList<CreditEntry> Libraries { get; } =
    [
        new CreditEntry("C#",                             "The main programming language",             "https://learn.microsoft.com/dotnet/csharp/"),
        new CreditEntry(".NET 10",                        "MIT · Application runtime",                 "https://dotnet.microsoft.com/"),
        new CreditEntry("Avalonia UI",                    "MIT · Cross-platform XAML framework",       "https://avaloniaui.net"),
        new CreditEntry("mpv / libmpv",                   "GPLv2+ · Video player engine",              "https://mpv.io"),
        new CreditEntry("CommunityToolkit.Mvvm",          "MIT · MVVM source generators",              "https://github.com/CommunityToolkit/dotnet"),
        new CreditEntry("Material.Icons.Avalonia",        "MIT · SKProCH",                             "https://github.com/SKProCH/Material.Icons"),
        new CreditEntry("AsyncImageLoader.Avalonia",      "MIT · Async image source",                  "https://github.com/AvaloniaUtils/AsyncImageLoader.Avalonia"),
        new CreditEntry("Microsoft.Extensions",           "MIT · Dependency injection & HTTP client factories", "https://github.com/dotnet/runtime"),
        new CreditEntry("Entity Framework Core (SQLite)", "MIT · Microsoft",                           "https://learn.microsoft.com/ef/core/"),
        new CreditEntry("Serilog",                        "Apache 2.0 · Structured logging",           "https://serilog.net"),
        new CreditEntry("Velopack",                       "MIT · Auto-update framework",               "https://velopack.io"),
        new CreditEntry("DiscordRichPresence",            "MIT · Lachee",                              "https://github.com/Lachee/discord-rpc-csharp"),
        new CreditEntry("AnitomySharp",                   "MPL 2.0 · Filename parsing",                "https://github.com/erengy/anitomy"),
        new CreditEntry("Anisthesia",                     "MPL 2.0 · Local media tracking",            "https://github.com/erengy/anisthesia"),
    ];
}
