using Kiriha.Core.Domain.Models.Api;
namespace Kiriha.Core.Domain.Constants;

public static class AppConstants
{
    public static class Api
    {
        public const string RedirectUri = "http://localhost:8080/";
        // NOTE: UserAgent moved to Kiriha.Core.AppInfo (auto-resolved from assembly version).
        // NOTE: MAL client_id is at Kiriha.Core.ApiKeys.MalClientId — do not re-add an alias.

        public static class Mal
        {
            public const string BaseUrl = "https://api.myanimelist.net/v2/";
            public const string TokenUrl = "https://myanimelist.net/v1/oauth2/token";
            public const string AuthUrl = "https://myanimelist.net/v1/oauth2/authorize";
            public const string WebsiteUrl = "https://myanimelist.net/anime/";
            public const string BaseWebsiteUrl = "https://myanimelist.net/";
            public const string AnimeListUrl = "https://myanimelist.net/animelist";
            public const string MangaListUrl = "https://myanimelist.net/mangalist";
            public const string NotLoggedIn = "Not logged in";
        }

        // shikimori.one and shikimori.net are independent OAuth realms with identical
        // contracts. We collapse the duplicated four-URL block into a single record
        // and expose two named instances. ShikiEndpoints.cs picks one based on the
        // active mirror in settings; OAuth token exchange runs directly from the
        // user's machine (see ApiKeys.cs for the WAF-bypass rationale).
        public static class Shiki
        {
            public const string OneHost = "https://shikimori.one";
            public const string NetHost = "https://shikimori.net";

            public static readonly ShikiHost One = new(
                BaseUrl: "https://shikimori.one/api/",
                TokenUrl: "https://shikimori.one/oauth/token",
                AuthUrl: "https://shikimori.one/oauth/authorize",
                WebsiteUrl: "https://shikimori.one/animes/",
                MangaWebsiteUrl: "https://shikimori.one/mangas/");

            public static readonly ShikiHost Net = new(
                BaseUrl: "https://shikimori.net/api/",
                TokenUrl: "https://shikimori.net/oauth/token",
                AuthUrl: "https://shikimori.net/oauth/authorize",
                WebsiteUrl: "https://shikimori.net/animes/",
                MangaWebsiteUrl: "https://shikimori.net/mangas/");
        }

        public static class AniList
        {
            public const string BaseUrl = "https://graphql.anilist.co";
        }

        public static class Jikan
        {
            public const string BaseUrl = "https://api.jikan.moe/v4/";
        }

        public static class Nyaa
        {
            public const string BaseUrl = "https://nyaa.si/";
            public const string XmlNamespace = "https://nyaa.si/xmlns/nyaa";
        }
    }

    public static class Links
    {
        public const string GitHubRepo = "https://github.com/donate684/kiriha";
        public const string GitHubReleases = "https://github.com/donate684/kiriha/releases";
    }

    public static class AiringStatus
    {
        public const string FinishedAiring = "finished_airing";
        public const string FinishedAiringSpaced = "finished airing";
        public const string CurrentlyAiring = "currently_airing";
        public const string CurrentlyAiringSpaced = "currently airing";
        public const string NotYetAired = "not_yet_aired";
        public const string NotYetAiredSpaced = "not yet aired";
        public const string Anons = "anons";

        public static bool IsFinishedAiring(string? statusDetailed)
        {
            if (string.IsNullOrWhiteSpace(statusDetailed)) return false;
            return statusDetailed.Equals(FinishedAiring, StringComparison.OrdinalIgnoreCase)
                || statusDetailed.Equals(FinishedAiringSpaced, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static class AnimeTypes
    {
        public const string Tv = "tv";
        public const string Movie = "movie";
        public const string Ova = "ova";
        public const string Ona = "ona";
        public const string Special = "special";
        public const string TvSpecial = "tv_special";

        public static readonly string[] TagPrefixes = ["type:", "format:", "тип:", "формат:"];

        public static readonly Models.Formats.FormatDefinition[] Definitions =
        [
            new(Movie, "Movie", "Фильм", ["фильм", "мувик", "полнометражка", "полнометражный", "film"]),
            new(Ova, "OVA", "OVA", ["ова", "овашка", "овашka"]),
            new(Ona, "ONA", "ONA", ["она", "веб"]),
            new(Special, "Special", "Спешл", ["спешл", "спецвыпуск", "тв_спешл", "tv_special"]),
            new(Tv, "TV Series", "ТВ Сериал", ["тв", "сериал", "тв сериал", "series"]),
        ];
    }

    public static class Sorting
    {
        public const string Popularity = "Popularity";
        public const string Score = "Score";
        public const string Title = "Title";
        public const string RussianTitle = "RussianTitle";
        public const string Date = "Date";
    }

    public static class Seasons
    {
        public const string Winter = "winter";
        public const string Spring = "spring";
        public const string Summer = "summer";
        public const string Fall = "fall";
    }

    public static class Languages
    {
        public const string En = "en";
        public const string Ru = "ru";
        public const string EnName = "English";
        public const string RuName = "Русский";
    }

    public static class System
    {
        public const string AppName = "Kiriha";
        public const string MutexName = "Kiriha_SingleInstance_Mutex";
        public const string AppStartedLog = "--- Application Starting ---";

        public static class FileNames
        {
            public const string Database = "kiriha.db";
            public const string Settings = "config.json";
            public const string LegacySettings = "config.json";
            public const string SettingsDir = "settings";
            public const string SettingsApp = "app.json";
            public const string SettingsPlayer = "player.json";
            public const string SettingsTorrents = "torrents.json";
            public const string SettingsAuth = "auth.json";
            public const string SettingsWindow = "window.json";
            public const string Mappings = "title_mappings.json";
            public const string LogsDir = "logs";
            public const string CacheDir = "cacheimg";
            public const string MpvWatchLaterDir = "watch_later";
        }
    }

    public static class Parsing
    {
        public const string BlockedByRoskomnadzor = "Заблокировано по требованию Роскомнадзора";
        public const string CyrillicChe = "Ч";
    }

    public static class Genres
    {
        public static readonly string[] TagPrefixes = ["tag:", "genre:", "тег:", "жанр:"];

        public static readonly Models.Genres.GenreDefinition[] Definitions =
        [
            new("ecchi", "Ecchi", "Эччи", ["этти", "етти", "echhi", "эти"]),
            new("drama", "Drama", "Драма", ["драматическое", "dram"]),
            new("comedy", "Comedy", "Комедия", ["юмор", "комедии"]),
            new("action", "Action", "Экшен", ["экшн", "боевик"]),
            new("adventure", "Adventure", "Приключения", ["приключение"]),
            new("fantasy", "Fantasy", "Фэнтези", ["фентези"]),
            new("romance", "Romance", "Романтика", ["романтическое", "любовь"]),
            new("slice_of_life", "Slice of Life", "Повседневность", ["слайс", "slice"]),
            new("sci_fi", "Sci-Fi", "Научная фантастика", ["scifi", "sci fi", "сайфай", "фантастика"]),
            new("supernatural", "Supernatural", "Сверхъестественное", ["сверхъестесственное"]),
            new("mystery", "Mystery", "Мистика", ["тайна"]),
            new("horror", "Horror", "Ужасы", ["хоррор"]),
            new("psychological", "Psychological", "Психологическое", ["психология"]),
            new("thriller", "Thriller", "Триллер"),
            new("isekai", "Isekai", "Исекай", ["исэкай", "попаданцы"]),
            new("harem", "Harem", "Гарем"),
            new("mecha", "Mecha", "Меха", ["роботы", "мех"]),
            new("military", "Military", "Военное", ["война", "армия"]),
            new("music", "Music", "Музыка", ["музыкальное"]),
            new("parody", "Parody", "Пародия"),
            new("school", "School", "Школа", ["школьное", "школьная"]),
            new("space", "Space", "Космос", ["космическое"]),
            new("sports", "Sports", "Спорт", ["спортивное"]),
            new("super_power", "Super Power", "Суперспособности", ["суперсила", "способности"]),
            new("vampire", "Vampire", "Вампиры", ["вампир"]),
            new("historical", "Historical", "Историческое", ["история"]),
            new("gourmet", "Gourmet", "Кулинария", ["еда", "готовка"]),
            new("magic", "Magic", "Магия", ["волшебство", "магическое"]),
            new("martial_arts", "Martial Arts", "Боевые искусства", ["драки"]),
            new("shounen", "Shounen", "Сёнен", ["сенен", "shonen"]),
            new("shoujo", "Shoujo", "Сёдзё", ["седзе", "shojo"]),
            new("seinen", "Seinen", "Сэйнэн", ["сейнен"]),
            new("josei", "Josei", "Дзёсей", ["дзесей"]),
            new("police", "Police", "Полиция", ["полицейские"]),
            new("samurai", "Samurai", "Самураи", ["самурай"]),
            new("boys_love", "Boys Love", "Сёнен-ай", ["сенен-ай", "сенен ай", "bl", "яой"]),
            new("girls_love", "Girls Love", "Сёдзё-ай", ["седзе-ай", "седзе ай", "gl", "юри"]),
            new("kids", "Kids", "Детское", ["дети"]),
            new("erotica", "Erotica", "Эротика"),
            new("award_winning", "Award Winning", "Удостоено наград", ["награды"]),
            new("avant_garde", "Avant Garde", "Авангард"),
            new("suspense", "Suspense", "Саспенс"),
            new("hentai", "Hentai", "Хентай"),
            new("adult_cast", "Adult Cast", "Взрослые персонажи", ["взрослые"]),
            new("anthropomorphic", "Anthropomorphic", "Антропоморфизм", ["фурри", "зверолюди"]),
            new("cgdct", "CGDCT", "Милые девочки делают милые вещи", ["милые девочки", "кют"]),
            new("childcare", "Childcare", "Воспитание детей", ["воспитание"]),
            new("combat_sports", "Combat Sports", "Единоборства"),
            new("crossdressing", "Crossdressing", "Кроссдрессинг", ["трапы", "переодевание"]),
            new("delinquents", "Delinquents", "Хулиганы", ["гопники", "бандиты"]),
            new("detective", "Detective", "Детектив", ["расследование"]),
            new("educational", "Educational", "Образовательное", ["обучающее"]),
            new("gag_humor", "Gag Humor", "Гэг-юмор", ["гэги"]),
            new("gore", "Gore", "Гуро", ["расчлененка", "кровь"]),
            new("high_stakes_game", "High Stakes Game", "Игры с высокими ставками", ["азартные игры"]),
            new("idols_female", "Idols (Female)", "Айдолы (девушки)", ["айдолы"]),
            new("idols_male", "Idols (Male)", "Айдолы (парни)"),
            new("iyashikei", "Iyashikei", "Иясикэй", ["исцеление"]),
            new("love_polygon", "Love Polygon", "Любовный многоугольник", ["треугольник"]),
            new("love_status_quo", "Love Status Quo", "Любовный статус-кво"),
            new("magical_sex_shift", "Magical Sex Shift", "Магическая смена пола", ["смена пола"]),
            new("mahou_shoujo", "Mahou Shoujo", "Махо-сёдзё", ["махо седзе", "волшебницы"]),
            new("organized_crime", "Organized Crime", "Криминал", ["мафия", "якудза"]),
            new("otaku_culture", "Otaku Culture", "Отаку-культура", ["отаку"]),
            new("performing_arts", "Performing Arts", "Сценическое искусство", ["театр", "сцена"]),
            new("pets", "Pets", "Питомцы", ["животные"]),
            new("racing", "Racing", "Гонки", ["авто"]),
            new("reincarnation", "Reincarnation", "Перерождение", ["реинкарнация"]),
            new("reverse_harem", "Reverse Harem", "Обратный гарем", ["реверс-гарем"]),
            new("showbiz", "Showbiz", "Шоу-бизнес"),
            new("strategy_game", "Strategy Game", "Стратегические игры", ["стратегия"]),
            new("survival", "Survival", "Выживание", ["выживач"]),
            new("team_sports", "Team Sports", "Командные виды спорта", ["командный спорт"]),
            new("time_travel", "Time Travel", "Путешествия во времени", ["таймтревел", "петля времени"]),
            new("urban_fantasy", "Urban Fantasy", "Городское фэнтези", ["городское фентези"]),
            new("video_game", "Video Game", "Видеоигры", ["игры", "гейминг"]),
            new("villainess", "Villainess", "Злодейка", ["злодейки"]),
            new("visual_arts", "Visual Arts", "Изобразительное искусство", ["рисование", "арт"]),
            new("workplace", "Workplace", "Рабочие будни", ["работа", "офис"]),
            new("medical", "Medical", "Медицина", ["врачи"]),
            new("mythology", "Mythology", "Мифология", ["мифы"])
        ];
    }
}
