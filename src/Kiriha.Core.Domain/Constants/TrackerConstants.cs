namespace Kiriha.Core.Domain.Constants;

public static class TrackerConstants
{
    public static class Ids
    {
        public const string Mal = "mal";
        public const string ShikiOrig = "shiki-orig";
        public const string ShikiFork = "shiki-fork";
        public const string AniList = "anilist";

        // Aliases for compatibility
        public const string ShikiOne = ShikiOrig;
        public const string ShikiNet = ShikiFork;
    }

    public static class Names
    {
        public const string Mal = "MyAnimeList";
        public const string ShikiOrig = "Shikimori (Original)";
        public const string ShikiFork = "Shikimori (Fork)";
        public const string AniList = "AniList";
        public const string ShikiGeneral = "Shikimori";
    }

    public static class Domains
    {
        public const string Mal = "myanimelist.net";
        public const string AniList = "anilist.co";
        public const string ShikiOrig = "shikimori.one";
        public const string ShikiFork = "shikimori.rip";
    }

    public static string GetDefaultDisplayName(string trackerId) => trackerId switch
    {
        Ids.Mal => Names.Mal,
        Ids.ShikiOrig => Names.ShikiOrig,
        Ids.ShikiFork => Names.ShikiFork,
        Ids.AniList => Names.AniList,
        _ => trackerId
    };
}
