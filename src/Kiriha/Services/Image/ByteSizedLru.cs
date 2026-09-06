using System;
using System.Collections.Generic;
using System.Threading;

namespace Kiriha.Services.Data.Image;

/// <summary>
/// Generic byte-sized LRU. Single-mutex; lookups are O(1) and keep the cache
/// hot-path on a couple of dictionary + linked-list operations. Items larger
/// than the entire budget are silently dropped instead of evicting everything
/// on a single insert.
/// </summary>
internal sealed class ByteSizedLru<TKey, TVal>
    where TKey : notnull
    where TVal : class
{
    private readonly long _budget;
    private readonly Func<TVal, long> _sizer;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map = new();
    private readonly LinkedList<Entry> _order = new();
    private readonly Lock _gate = new();
    private long _used;

    public ByteSizedLru(long budgetBytes, Func<TVal, long> sizer)
    {
        _budget = budgetBytes;
        _sizer = sizer;
    }

    public bool TryGet(TKey key, out TVal? value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = null;
            return false;
        }
    }

    public void Set(TKey key, TVal value)
    {
        long size = _sizer(value);
        if (size <= 0 || size > _budget) return;

        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _used -= _sizer(existing.Value.Value);
                _order.Remove(existing);
                _map.Remove(key);
            }

            var node = new LinkedListNode<Entry>(new Entry(key, value));
            _order.AddFirst(node);
            _map[key] = node;
            _used += size;

            while (_used > _budget && _order.Last is { } tail)
            {
                _used -= _sizer(tail.Value.Value);
                _map.Remove(tail.Value.Key);
                _order.RemoveLast();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _order.Clear();
            _used = 0;
        }
    }

    private readonly record struct Entry(TKey Key, TVal Value);
}
