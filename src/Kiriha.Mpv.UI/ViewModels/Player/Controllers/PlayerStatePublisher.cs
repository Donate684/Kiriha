using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Mpv.UI.Services;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public sealed class PlayerStatePublisher : IDisposable
{
    private readonly InternalPlayerStateClient _client = new();
    private readonly Func<InternalPlayerState> _createState;

    public event Action<PlayerMediaMetadata>? MetadataReceived
    {
        add => _client.MetadataReceived += value;
        remove => _client.MetadataReceived -= value;
    }

    public PlayerStatePublisher(Func<InternalPlayerState> createState)
    {
        _createState = createState;
        _client.StateProvider = _createState;
    }

    public void Connect()
    {
        _client.Start();
    }

    public void Publish()
    {
        _client.Publish(_createState());
    }

    public void PublishClosed()
    {
        _client.PublishClosed();
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
