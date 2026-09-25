using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Kiriha.Core;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Models.Genres;
using Kiriha.Models;

namespace Kiriha.Core;

public sealed partial class AnimeCollectionProjection
{
    private void AddItems(IEnumerable<AnimeEntity>? items)
    {
        if (items is null) return;
        foreach (var item in items) Add(item);
    }

    private void RemoveItems(IEnumerable<AnimeEntity>? items)
    {
        if (items is null) return;
        foreach (var item in items) Remove(item);
    }

    private void Add(AnimeEntity item)
    {
        Remove(item);

        var entry = Entry.From(item);
        _entriesById[item.Id] = entry;

        if (!_buckets.TryGetValue((entry.ListStatus, entry.Kind), out var bucket))
        {
            bucket = new Dictionary<int, Entry>();
            _buckets[(entry.ListStatus, entry.Kind)] = bucket;
        }
        bucket[item.Id] = entry;

        item.PropertyChanged += OnItemPropertyChanged;
    }

    private void Remove(AnimeEntity item)
    {
        if (!_entriesById.Remove(item.Id, out var entry)) return;

        if (_buckets.TryGetValue((entry.ListStatus, entry.Kind), out var bucket))
        {
            bucket.Remove(item.Id);
        }

        item.PropertyChanged -= OnItemPropertyChanged;
    }

    private void Clear()
    {
        foreach (var entry in _entriesById.Values)
        {
            entry.Item.PropertyChanged -= OnItemPropertyChanged;
        }

        _entriesById.Clear();
        foreach (var bucket in _buckets.Values)
        {
            bucket.Clear();
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not AnimeEntity item) return;

        if (string.IsNullOrEmpty(e.PropertyName) || AffectsProjection(e.PropertyName))
        {
            lock (_syncLock)
            {
                Add(item);
            }
        }
    }

    private static bool AffectsProjection(string propertyName)
    {
        return propertyName is nameof(AnimeEntity.Title)
            or nameof(AnimeEntity.RussianTitle)
            or nameof(AnimeEntity.EnglishTitle)
            or nameof(AnimeEntity.JapaneseTitle)
            or nameof(AnimeEntity.Rating)
            or nameof(AnimeEntity.Status)
            or nameof(AnimeEntity.IsRewatching)
            or nameof(AnimeEntity.MediaKind)
            or nameof(AnimeEntity.Genres)
            or nameof(AnimeEntity.Type);
    }

    private static UserAnimeStatus GetListStatus(AnimeEntity item)
    {
        return item.Status == UserAnimeStatus.Watching || item.IsRewatching
            ? UserAnimeStatus.Watching
            : item.Status;
    }

    private static string BuildSearchableText(AnimeEntity item)
    {
        return Normalize(string.Join('\n',
        [
            item.Title,
            item.RussianTitle,
            item.EnglishTitle,
            item.JapaneseTitle
        ]));
    }

    private static IReadOnlySet<string> ComputeGenreKeys(AnimeEntity item)
    {
        if (item.Genres == null || item.Genres.Count == 0) return FrozenSet<string>.Empty;

        var keys = new HashSet<string>(item.Genres.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var g in item.Genres)
        {
            if (GenreCatalog.TryFindGenre(g, out var def) && def != null)
            {
                keys.Add(def.Key);
            }
            else
            {
                var norm = GenreCatalog.NormalizeLookup(g);
                if (norm.Length > 0) keys.Add(norm);
            }
        }

        return keys.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool ComputeIsNsfw(AnimeEntity item) => item.IsNsfw;

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.ToUpperInvariant();
    }

    private sealed record Entry(
        AnimeEntity Item,
        UserAnimeStatus ListStatus,
        string SearchableText,
        IReadOnlySet<string> GenreKeys,
        bool IsNsfw,
        MediaKind Kind)
    {
        public static Entry From(AnimeEntity item)
        {
            return new Entry(item, GetListStatus(item), BuildSearchableText(item), ComputeGenreKeys(item), ComputeIsNsfw(item), item.MediaKind);
        }
    }
}
