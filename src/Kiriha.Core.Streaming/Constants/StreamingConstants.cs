namespace Kiriha.Core.Streaming.Constants;

/// <summary>
/// Constants and endpoint URLs for online streaming providers and video extractors.
/// </summary>
public static class StreamingConstants
{
    public const string HttpsPrefix = "https://";
    public const string HttpPrefix = "http://";

    public static class Providers
    {
        public const string AniLibria = "AniLibria";
        public const string AnimeLib = "AnimeLib";
        public const string DreamCast = "DreamCast";
        public const string YummyAnime = "YummyAnime";
        public const string Anime365 = "Anime365";
    }

    public static class Anime365
    {
        public const string DefaultAppId = "app-f77050293d1e92794d78fc1e";

        public const string MirrorCo = "smotret-anime.co";
        public const string MirrorOrg = "smotret-anime.org";
        public const string MirrorSmotretAnimeCo = "smotretanime.co";
        public const string MirrorCom = "smotret-anime.com";
        public const string MirrorApp = "smotret-anime.app";

        public const string DefaultMirror = MirrorCo;

        public static readonly IReadOnlyList<string> KnownMirrors =
        [
            MirrorCo,
            MirrorOrg,
            MirrorSmotretAnimeCo,
            MirrorCom,
            MirrorApp
        ];

        public const string BaseUrl = "https://smotret-anime.co/api";
        public const string SeriesEndpoint = "https://smotret-anime.co/api/series";
        public const string EpisodesEndpoint = "https://smotret-anime.co/api/episodes";
        public const string TranslationsEndpoint = "https://smotret-anime.co/api/translations";
        public const string EmbedEndpoint = "https://smotret-anime.co/api/translations/embed";
        public const string LoginEndpoint = "https://smotret-anime.co/api/login";
        public const string AccessTokenEndpoint = "https://smotret-anime.co/api/accessToken";
        public const string MeEndpoint = "https://smotret-anime.co/api/me";
        public const string EmbedPath = "/translations/embed/";
        public const string EmbedWebUrlPrefix = "https://smotret-anime.co/translations/embed/";
        public const string SiteUrl = "https://smotret-anime.co";
    }

    public static class AniLibria
    {
        public const string StaticHost = "https://aniliberty.top";
        public const string BaseUrl = "https://aniliberty.top/api/v1";
        public const string SearchEndpoint = "https://aniliberty.top/api/v1/app/search/releases";
        public const string CatalogReleasesEndpoint = "https://aniliberty.top/api/v1/anime/catalog/releases";
        public const string ReleaseDetailEndpoint = "https://aniliberty.top/api/v1/anime/releases";
    }

    public static class AnimeLib
    {
        public const string BaseUrl = "https://api.cdnlibs.org/api";
        public const string SearchEndpoint = "https://api.cdnlibs.org/api/anime";
        public const string EpisodesEndpoint = "https://api.cdnlibs.org/api/episodes";
        public const string SiteUrl = "https://animelib.org";
        public const string RefererUrl = "https://animelib.org/";
        public const string LoginUrl = "https://animelib.org/ru/front/auth";
        public const string VideoServerMain = "https://video1.cdnlibs.org/.%D0%B0s/";
        public const string VideoServerSecondary1 = "https://video1.cdnlibs.org/.%D0%B0s/";
        public const string VideoServerSecondary2 = "https://video1.cdnlibs.org/.%D0%B0s/";
        public const string SubtitlesServer = "https://api.cdnlibs.org";
        public const string SubtitlesPathPrefix = "/uploads/subtitles/";
    }

    public static class DreamCast
    {
        public const string BaseUrl = "https://dreamerscast.com";
        public const string RefererUrl = "https://dreamerscast.com/";
    }

    public static class YummyAnime
    {
        public const string BaseUrl = "https://api.yani.tv";
        public const string SearchEndpoint = "https://api.yani.tv/anime";
    }

    public static class Timeouts
    {
        public const int HttpClientTimeoutSeconds = 5;
        public const int ProviderSearchTimeoutSeconds = 4;
    }

    public static class Aniboom
    {
        public const string BaseUrl = "https://aniboom.one";
        public const string RefererUrl = "https://aniboom.one/";
        public const string AnimeGoReferer = "https://animego.me/";
    }

    public static class Sibnet
    {
        public const string BaseUrl = "https://video.sibnet.ru";
    }

    public static class Headers
    {
        public const string Referer = "Referer";
        public const string Origin = "Origin";
        public const string UserAgent = "User-Agent";
        public const string AcceptLanguage = "Accept-Language";
        public const string Accept = "Accept";

        public const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
        public const string RuRuLanguage = "ru-RU";
        public const string JsonAccept = "application/json, text/javascript, */*; q=0.01";
        public const string Authorization = "Authorization";
        public const string SiteId = "Site-Id";
        public const string SiteIdAnimeLib = "5";
    }
}
