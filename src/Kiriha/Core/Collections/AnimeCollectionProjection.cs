using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core;

public sealed partial class AnimeCollectionProjection : IDisposable
{
    private readonly Lock _syncLock = new();
    private readonly Dictionary<int, Entry> _entriesById = new();
    private readonly Dictionary<(UserAnimeStatus Status, MediaKind Kind), Dictionary<int, Entry>> _buckets = new();

    public AnimeCollectionProjection()
    {
        InitializeBuckets();
    }

    private void InitializeBuckets()
    {
        var statuses = (UserAnimeStatus[])Enum.GetValues(typeof(UserAnimeStatus));
        var kinds = (MediaKind[])Enum.GetValues(typeof(MediaKind));

        foreach (var s in statuses)
        {
            foreach (var k in kinds)
            {
                _buckets[(s, k)] = new Dictionary<int, Entry>();
            }
        }
    }

    public void Rebuild(IEnumerable<AnimeEntity> items)
    {
        lock (_syncLock)
        {
            Clear();

            foreach (var item in items)
            {
                Add(item);
            }
        }
    }

    public void ApplyCollectionChange(NotifyCollectionChangedEventArgs e, IEnumerable<AnimeEntity> currentItems)
    {
        lock (_syncLock)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    AddItems(e.NewItems?.OfType<AnimeEntity>());
                    break;
                case NotifyCollectionChangedAction.Remove:
                    RemoveItems(e.OldItems?.OfType<AnimeEntity>());
                    break;
                case NotifyCollectionChangedAction.Replace:
                    RemoveItems(e.OldItems?.OfType<AnimeEntity>());
                    AddItems(e.NewItems?.OfType<AnimeEntity>());
                    break;
                case NotifyCollectionChangedAction.Move:
                    break;
                default:
                    Rebuild(currentItems);
                    break;
            }
        }
    }

    public int Count(UserAnimeStatus status, MediaKind kind)
    {
        lock (_syncLock)
        {
            return _buckets.TryGetValue((status, kind), out var bucket)
                ? bucket.Count
                : 0;
        }
    }

    public List<AnimeEntity> Query(UserAnimeStatus status, string? searchQuery, bool filterNsfw, string? sortBy, MediaKind kind, bool prioritizeNewEpisodes = false)
    {
        lock (_syncLock)
        {
            if (!_buckets.TryGetValue((status, kind), out var bucket) || bucket.Count == 0)
            {
                return new List<AnimeEntity>();
            }

            var normalizedSearch = Normalize(searchQuery);
            var result = new List<AnimeEntity>(bucket.Count);

            foreach (var entry in bucket.Values)
            {
                if (normalizedSearch.Length > 0 && !entry.SearchableText.Contains(normalizedSearch, StringComparison.Ordinal))
                {
                    continue;
                }

                if (filterNsfw ? !entry.IsNsfw : entry.IsNsfw)
                {
                    continue;
                }

                result.Add(entry.Item);
            }

            result.SortInPlace(sortBy, isSeasonal: false, prioritizeNewEpisodes: prioritizeNewEpisodes);
            return result;
        }
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            Clear();
        }
    }
}
