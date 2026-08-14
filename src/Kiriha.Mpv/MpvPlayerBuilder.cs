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
            player.VideoPipelineConfigurator.ConfigureVideoPipeline(handle, options ?? MpvOptions.Default);

            // Ensure mpv does not quit automatically on playback end or error
            MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "idle", "yes"), "enable idle");
            MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "keep-open", "yes"), "enable keep-open");

            // Keep the embedded player modest: mpv defaults are tuned for a full player,
            // while Kiriha mostly needs enough buffer for smooth anime playback.
            MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "demuxer-max-bytes", "64MiB"), "limit demuxer cache");
            MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "demuxer-max-back-bytes", "16MiB"), "limit back buffer");
            player.ScreenshotManager.ConfigureScreenshots(handle);

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
