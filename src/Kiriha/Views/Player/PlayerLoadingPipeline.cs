using Kiriha.Mpv;
using Kiriha.Mpv.UI.ViewModels.Player;
using Kiriha.Views.Controls;
using Serilog;

namespace Kiriha.Views.Player;

internal sealed class PlayerLoadingPipeline
{
    private readonly PlayerViewModel _viewModel;
    private readonly MpvOpenGlVideoView _videoView;
    private bool _uiSubscribed;
    private bool _mediaLoadStarted;
    private bool _renderContextReady;
    private bool _initialFrameRequested;

    public PlayerLoadingPipeline(PlayerViewModel viewModel, MpvOpenGlVideoView videoView)
    {
        _viewModel = viewModel;
        _viewModel.WaitForRenderContext();
        _videoView = videoView;
    }

    public void AttachPlayer(MpvPlayer player)
    {
        if (_uiSubscribed)
            return;

        _viewModel.Initialize(player);
        _videoView.Player = player;
        NotifyUiSubscribed();
    }

    public void NotifyUiSubscribed()
    {
        _uiSubscribed = true;
        TryStartMediaLoad();
    }

    public void MarkRenderContextReady()
    {
        _renderContextReady = true;
        TryStartMediaLoad();
        TryRequestInitialFrame();
    }

    private void TryStartMediaLoad()
    {
        if (!_uiSubscribed ||
            !_renderContextReady ||
            _mediaLoadStarted)
            return;

        _mediaLoadStarted = true;
        if (!string.IsNullOrWhiteSpace(_viewModel.VideoUrl))
            Log.Information("Loading video: {Url}", _viewModel.VideoUrl);
        _viewModel.StartInitialPlayback();
        TryRequestInitialFrame();
    }

    private void TryRequestInitialFrame()
    {
        if (_initialFrameRequested || !_mediaLoadStarted || !_renderContextReady || string.IsNullOrWhiteSpace(_viewModel.VideoUrl))
            return;

        _initialFrameRequested = true;
        _videoView.RequestNextFrameRendering();
    }
}
