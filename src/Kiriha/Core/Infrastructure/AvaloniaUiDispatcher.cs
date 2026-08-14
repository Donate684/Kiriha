using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Infrastructure;

namespace Kiriha.Infrastructure;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public async Task InvokeAsync(Action action)
        => await Dispatcher.UIThread.InvokeAsync(action);

    public async Task<T> InvokeAsync<T>(Func<T> action)
        => await Dispatcher.UIThread.InvokeAsync(action);

    public async Task InvokeAsync(Func<Task> action)
        => await Dispatcher.UIThread.InvokeAsync(action);

    public void Post(Action action)
        => Dispatcher.UIThread.Post(action);
}
