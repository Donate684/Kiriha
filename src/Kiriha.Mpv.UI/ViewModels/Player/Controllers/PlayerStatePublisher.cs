using System;
using Kiriha.Mpv.UI.Services;
using Kiriha.Core.Domain.Models.Api;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public sealed class PlayerStatePublisher : IDisposable
{
    private readonly InternalPlayerStateClient _client = new();
    private readonly Func<InternalPlayerState> _createState;

    public PlayerStatePublisher(Func<InternalPlayerState> createState)
    {
        _createState = createState;
    }

    public void Connect()
    {
        _ = _client.ConnectAsync();
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
