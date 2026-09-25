using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Models.Genres;
using Kiriha.Models;

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

    public int CountGenre(UserAnimeStatus status, MediaKind kind, string genreKey, bool filterNsfw = false)
    {
        lock (_syncLock)
        {
            if (!_buckets.TryGetValue((status, kind), out var bucket) || bucket.Count == 0) return 0;
            int count = 0;
            foreach (var entry in bucket.Values)
            {
                if (filterNsfw ? !entry.IsNsfw : entry.IsNsfw) continue;
                if (entry.GenreKeys.Contains(genreKey)) count++;
            }
            return count;
        }
    }

    public int CountFormat(UserAnimeStatus status, MediaKind kind, string formatKey, bool filterNsfw = false)
    {
        lock (_syncLock)
        {
            if (!_buckets.TryGetValue((status, kind), out var bucket) || bucket.Count == 0) return 0;
            int count = 0;
            foreach (var entry in bucket.Values)
            {
                if (filterNsfw ? !entry.IsNsfw : entry.IsNsfw) continue;
                if (Kiriha.Core.Domain.Models.Formats.FormatCatalog.MatchesFormat(entry.Item.Type, formatKey)) count++;
            }
            return count;
        }
    }

    public List<AnimeEntity> Query(UserAnimeStatus status, string? searchQuery, bool filterNsfw, string? sortBy, MediaKind kind, bool prioritizeNewEpisodes = false)
        => Query(status, searchQuery, null, null, filterNsfw, sortBy, kind, prioritizeNewEpisodes);

    public List<AnimeEntity> Query(
        UserAnimeStatus status,
        string? searchQuery,
        IReadOnlyCollection<string>? activeGenreKeys,
        bool filterNsfw,
        string? sortBy,
        MediaKind kind,
        bool prioritizeNewEpisodes = false)
        => Query(status, searchQuery, activeGenreKeys, null, filterNsfw, sortBy, kind, prioritizeNewEpisodes);

    public List<AnimeEntity> Query(
        UserAnimeStatus status,
        string? searchQuery,
        IReadOnlyCollection<string>? activeGenreKeys,
        IReadOnlyCollection<string>? activeFormatKeys,
        bool filterNsfw,
        string? sortBy,
        MediaKind kind,
        bool prioritizeNewEpisodes = false)
    {
        lock (_syncLock)
        {
            if (!_buckets.TryGetValue((status, kind), out var bucket) || bucket.Count == 0)
            {
                return [];
            }

            var parsed = AnimeSearchQueryParser.Parse(searchQuery);

            HashSet<string>? requiredGenreKeys = null;
            if (activeGenreKeys != null && activeGenreKeys.Count > 0)
            {
                requiredGenreKeys = new HashSet<string>(activeGenreKeys, StringComparer.OrdinalIgnoreCase);
            }
            if (parsed.ExtractedGenreKeys.Count > 0)
            {
                requiredGenreKeys ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var k in parsed.ExtractedGenreKeys)
                {
                    requiredGenreKeys.Add(k);
                }
            }

            HashSet<string>? requiredFormatKeys = null;
            if (activeFormatKeys != null && activeFormatKeys.Count > 0)
            {
                requiredFormatKeys = new HashSet<string>(activeFormatKeys, StringComparer.OrdinalIgnoreCase);
            }
            if (parsed.ExtractedFormatKeys.Count > 0)
            {
                requiredFormatKeys ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var k in parsed.ExtractedFormatKeys)
                {
                    requiredFormatKeys.Add(k);
                }
            }

            var normalizedSearch = parsed.HasTitleSearch ? Normalize(parsed.TitleSearchText) : string.Empty;
            int initialCapacity = (normalizedSearch.Length > 0 || requiredGenreKeys != null || requiredFormatKeys != null)
                ? Math.Min(bucket.Count, 32)
                : bucket.Count;
            var result = new List<AnimeEntity>(initialCapacity);

            foreach (var entry in bucket.Values)
            {
                if (filterNsfw ? !entry.IsNsfw : entry.IsNsfw)
                {
                    continue;
                }

                if (requiredFormatKeys != null && requiredFormatKeys.Count > 0)
                {
                    bool matchesFormat = false;
                    foreach (var req in requiredFormatKeys)
                    {
                        if (Kiriha.Core.Domain.Models.Formats.FormatCatalog.MatchesFormat(entry.Item.Type, req))
                        {
                            matchesFormat = true;
                            break;
                        }
                    }
                    if (!matchesFormat) continue;
                }

                if (requiredGenreKeys != null && requiredGenreKeys.Count > 0)
                {
                    bool matchesGenres = true;
                    foreach (var req in requiredGenreKeys)
                    {
                        if (!entry.GenreKeys.Contains(req))
                        {
                            matchesGenres = false;
                            break;
                        }
                    }
                    if (!matchesGenres) continue;
                }

                if (normalizedSearch.Length > 0 && !entry.SearchableText.Contains(normalizedSearch, StringComparison.Ordinal))
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
