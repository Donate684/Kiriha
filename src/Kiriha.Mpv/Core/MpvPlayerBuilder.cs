using System;
using Kiriha.Mpv;

namespace Kiriha.Mpv;

public static class MpvPlayerBuilder
{
    public static MpvPlayer Build(MpvOptions? options = null)
    {
        var player = new MpvPlayer();
        
        try
        {
            var handle = player.MpvHandle;
            
            // Configure mpv for host-driven rendering.
            MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "osc", "no"), "disable osc");
            MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "input-default-bindings", "no"), "disable default input bindings");
            MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "input-vo-keyboard", "no"), "disable mpv keyboard input");
            
            var opts = options ?? MpvOptions.Default;
            player.VideoPipelineConfigurator.ConfigureVideoPipeline(handle, opts);

            if (!string.IsNullOrWhiteSpace(opts.VideoSync) && opts.VideoSync != "no")
                MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "video-sync", opts.VideoSync), "set video sync mode");
                
            if (opts.Interpolation)
            {
                MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "interpolation", "yes"), "enable interpolation");
                if (!string.IsNullOrWhiteSpace(opts.TemporalScale))
                    MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "tscale", opts.TemporalScale), "set temporal scale");
            }

            MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "opengl-swapinterval", "1"), "set swap interval");
            MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "gpu-shader-cache", "yes"), "enable shader cache");

            // Ensure mpv does not quit automatically on playback end or error
            MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "idle", "yes"), "enable idle");
            MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "keep-open", "yes"), "enable keep-open");

            if (opts.SavePositionOnQuit)
            {
                if (!string.IsNullOrWhiteSpace(opts.WatchLaterDirectory))
                {
                    try { System.IO.Directory.CreateDirectory(opts.WatchLaterDirectory); } catch { }
                    MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "watch-later-directory", opts.WatchLaterDirectory), "set watch later directory");
                }
                MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "save-position-on-quit", "yes"), "enable save-position-on-quit");
                MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "write-filename-in-watch-later-config", "yes"), "write filename in watch-later config");
                MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "watch-later-options", "start"), "set watch-later options to position only");
            }
            else
            {
                MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "save-position-on-quit", "no"), "disable save-position-on-quit");
                MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "resume-playback", "no"), "disable resume-playback");
            }

            // Keep the embedded player modest: mpv defaults are tuned for a full player,
            // while Kiriha mostly needs enough buffer for smooth anime playback.
            MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "demuxer-max-bytes", "32MiB"), "limit demuxer cache");
            MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "demuxer-max-back-bytes", "8MiB"), "limit back buffer");
            player.ScreenshotManager.ConfigureScreenshots(handle);

            var fontsDir = MpvPlayer.ResolveFontsDirectory();
            if (!string.IsNullOrEmpty(fontsDir))
            {
                MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "sub-fonts-dir", fontsDir), "set fonts directory");
            }

            int res = LibMpvNative.mpv_initialize(handle);
            if (res < 0)
            {
                throw new InvalidOperationException($"Failed to initialize mpv: {LibMpvNative.GetErrorString(res)}");
            }

            player.Initialize();
            return player;
        }
        catch
        {
            player.Dispose();
            throw;
        }
    }
}
