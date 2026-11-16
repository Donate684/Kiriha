using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Kiriha.Core.Abstractions.Services;

/// <summary>
/// Framework-agnostic in-process pub/sub bus for domain and tracking events,
/// keeping core assemblies free of UI/MVVM messaging toolkits.
/// </summary>
public interface ICoreEventBus
{
    void Publish<TEvent>(TEvent evt) where TEvent : class;
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
}

/// <summary>
/// Thread-safe, lock-free-read implementation of <see cref="ICoreEventBus"/>.
/// </summary>
public class CoreEventBus : ICoreEventBus
{
    public static CoreEventBus Default { get; } = new();

    private readonly ConcurrentDictionary<Type, ImmutableArray<Subscription>> _handlers = new();

    public virtual void Publish<TEvent>(TEvent evt) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (!_handlers.TryGetValue(typeof(TEvent), out var list) || list.IsEmpty)
            return;

        for (int i = 0; i < list.Length; i++)
        {
            if (list[i].Callback is Action<TEvent> typed)
            {
                typed(evt);
            }
        }
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(handler);

        var sub = new Subscription(typeof(TEvent), handler, Unsubscribe);
        _handlers.AddOrUpdate(
            typeof(TEvent),
            static (_, s) => [s],
            static (_, existing, s) => existing.Add(s),
            sub);

        return sub;
    }

    private void Unsubscribe(Subscription sub)
    {
        while (_handlers.TryGetValue(sub.EventType, out var existing))
        {
            var updated = existing.Remove(sub);
            if (updated == existing)
                return;

            if (_handlers.TryUpdate(sub.EventType, updated, existing))
                return;
        }
    }

    private sealed class Subscription(Type eventType, Delegate callback, Action<Subscription> onDispose) : IDisposable
    {
        private int _disposed;
        public Type EventType { get; } = eventType;
        public Delegate Callback { get; } = callback;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                onDispose(this);
            }
        }
    }
}
