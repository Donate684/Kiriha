namespace Kiriha.Mpv.Constants;

public static class MpvConstants
{
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    public const string AnimeLibReferer = "https://animelib.org/";

    public static class Options
    {
        public const string DemuxerReadaheadSecs = "demuxer-readahead-secs";
        public const string DemuxerMaxBytes = "demuxer-max-bytes";
        public const string DemuxerMaxBackBytes = "demuxer-max-back-bytes";
        public const string NetworkTimeout = "network-timeout";
        public const string StreamLavfOptions = "stream-lavf-o";
    }
}