using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Services;

namespace Kiriha.Services.Messaging;

/// <summary>
/// UI-layer <see cref="ICoreEventBus"/> adapter that dispatches events both to
/// core <see cref="ICoreEventBus"/> subscribers and to <see cref="WeakReferenceMessenger.Default"/>
/// for Avalonia ViewModels, keeping <c>Kiriha.Core.Tracking</c> free of MVVM dependencies.
/// </summary>
internal sealed class MessengerCoreEventBus : CoreEventBus
{
    public override void Publish<TEvent>(TEvent evt)
    {
        base.Publish(evt);
        WeakReferenceMessenger.Default.Send(evt);
    }
}
