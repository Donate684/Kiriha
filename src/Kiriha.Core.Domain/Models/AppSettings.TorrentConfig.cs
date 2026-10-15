namespace Kiriha.Core.Domain.Models;

public partial class AppSettings
{
    public class TorrentConfig
    {
        public bool OnlyCrunchyroll { get; set; } = false;
        public bool FilterNetflix { get; set; } = false;
        public bool FilterAmazon { get; set; } = false;
        public bool FilterHidive { get; set; } = false;
        public bool FilterVaryg { get; set; } = false;
        public bool FilterEraiRaws { get; set; } = false;
        public bool FilterToonsHub { get; set; } = false;
        public bool FilterJudas { get; set; } = false;
        public bool FilterHevc { get; set; } = false;
        public bool Filter1080p { get; set; } = false;
        [System.Text.Json.Serialization.JsonIgnore]
        public System.Collections.Generic.List<int> HiddenAnimeIds { get; set; } = new();

        /// <summary>When true, filter toggles are remembered per anime title.</summary>
        public bool FiltersPerTitle { get; set; } = true;

        /// <summary>When true, online streaming search mode is selected instead of torrents mode.</summary>
        public bool IsOnlineMode { get; set; } = false;

        [System.Text.Json.Serialization.JsonIgnore]
        public System.Collections.Generic.Dictionary<int, TorrentFilterSet> PerTitleFilters { get; set; } = new();

        public TorrentStreamingConfig Streaming { get; set; } = new();
        public OnlineStreamingConfig OnlineStreaming { get; set; } = new();
    }

    public class OnlineStreamingConfig
    {
        public bool EnableAniLibria { get; set; } = true;
        public bool EnableAnimeLib { get; set; } = false;
        public bool EnableDreamCast { get; set; } = false;
        public bool EnableYummyAnime { get; set; } = false;
        public bool EnableAnime365 { get; set; } = false;
        public bool Hide480p { get; set; } = false;
        public bool Hide720p { get; set; } = false;
        public bool HideNonRussian { get; set; } = false;
        public string AnimeLibToken { get; set; } = string.Empty;
        public string AnimeLibUsername { get; set; } = string.Empty;
        public string Anime365Token { get; set; } = string.Empty;
        public string Anime365Username { get; set; } = string.Empty;
        public string Anime365AppId { get; set; } = "app-f77050293d1e92794d78fc1e";
        public string Anime365Mirror { get; set; } = string.Empty;
        public bool PreferDirectMp4 { get; set; } = true;
        public string PreferredSourceType { get; set; } = "dub";
    }

    public class TorrentStreamingConfig
    {
        public bool Enabled { get; set; } = true;
        public string ServerUrl { get; set; } = Constants.AppConstants.Api.TorrServer.DefaultBaseUrl;
        public int RamCacheMb { get; set; } = 200;
        public bool AutoStartServer { get; set; } = true;
        public int ServerPort { get; set; } = Constants.AppConstants.Api.TorrServer.DefaultPort;
    }

    public class TorrentFilterSet
    {
        public bool OnlyCrunchyroll { get; set; }
        public bool FilterNetflix { get; set; }
        public bool FilterAmazon { get; set; }
        public bool FilterHidive { get; set; }
        public bool FilterVaryg { get; set; }
        public bool FilterEraiRaws { get; set; }
        public bool FilterToonsHub { get; set; }
        public bool FilterJudas { get; set; }
        public bool FilterHevc { get; set; }
        public bool Filter1080p { get; set; }
        public bool UseCustomQuery { get; set; }
        public string? CustomQuery { get; set; }
    }
}
