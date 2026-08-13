using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Kiriha.Models;
using Kiriha.Core.Abstractions.Models;
using Kiriha.Core.Abstractions.Models.Entities;

namespace Kiriha.Core;

public sealed partial class AnimeCollectionProjection : IDisposable
{
    private readonly Dictionary<int, Entry> _entriesById = new();
    private readonly Dictionary<UserAnimeStatus, Dictionary<int, Entry>> _buckets = new()
    {
        [UserAnimeStatus.None] = new(),
        [UserAnimeStatus.Watching] = new(),
        [UserAnimeStatus.Completed] = new(),
        [UserAnimeStatus.OnHold] = new(),
        [UserAnimeStatus.Dropped] = new(),
        [UserAnimeStatus.PlanToWatch] = new(),
    };
    private readonly Dictionary<UserAnimeStatus, Dictionary<MediaKind, int>> _counts = new()
    {
        [UserAnimeStatus.None] = new(),
        [UserAnimeStatus.Watching] = new(),
        [UserAnimeStatus.Completed] = new(),
        [UserAnimeStatus.OnHold] = new(),
        [UserAnimeStatus.Dropped] = new(),
        [UserAnimeStatus.PlanToWatch] = new(),
    };

    public void Rebuild(IEnumerable<AnimeEntity> items)
    {
        Clear();

        foreach (var item in items)
        {
            Add(item);
        }
    }

    public void ApplyCollectionChange(NotifyCollectionChangedEventArgs e, IEnumerable<AnimeEntity> currentItems)
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

    public int Count(UserAnimeStatus status, MediaKind kind)
    {
        return _counts.TryGetValue(status, out var countsByKind) && countsByKind.TryGetValue(kind, out var count)
            ? count
            : 0;
    }

    public List<AnimeEntity> Query(UserAnimeStatus status, string? searchQuery, bool filterNsfw, string? sortBy, MediaKind kind)
    {
        if (!_buckets.TryGetValue(status, out var bucket))
        {
            return new List<AnimeEntity>();
        }

        var normalizedSearch = Normalize(searchQuery);
        var query = bucket.Values.Where(x => x.Kind == kind);

        if (normalizedSearch.Length > 0)
        {
            query = query.Where(x => x.SearchableText.Contains(normalizedSearch, StringComparison.Ordinal));
        }

        query = filterNsfw
            ? query.Where(x => x.IsNsfw)
            : query.Where(x => !x.IsNsfw);

        return query.Select(x => x.Item).ApplySorting(sortBy).ToList();
    }

    public void Dispose()
    {
        Clear();
    }
}
