using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Kiriha.Models;
using Kiriha.Core.Abstractions.Models;
using Kiriha.Core.Abstractions.Models.Entities;

namespace Kiriha.Core;

public sealed partial class AnimeCollectionProjection
{
    private void AddItems(IEnumerable<AnimeEntity>? items)
    {
        if (items == null) return;
        foreach (var item in items) Add(item);
    }

    private void RemoveItems(IEnumerable<AnimeEntity>? items)
    {
        if (items == null) return;
        foreach (var item in items) Remove(item);
    }

    private void Add(AnimeEntity item)
    {
        Remove(item);

        var entry = Entry.From(item);
        _entriesById[item.Id] = entry;
        if (_buckets.TryGetValue(entry.ListStatus, out var bucket))
        {
            bucket[item.Id] = entry;

            if (!_counts[entry.ListStatus].TryGetValue(entry.Kind, out var count))
            {
                count = 0;
            }
            _counts[entry.ListStatus][entry.Kind] = count + 1;
        }

        item.PropertyChanged += OnItemPropertyChanged;
    }

    private void Remove(AnimeEntity item)
    {
        if (!_entriesById.Remove(item.Id, out var entry)) return;

        if (_buckets.TryGetValue(entry.ListStatus, out var bucket))
        {
            bucket.Remove(item.Id);

            if (_counts[entry.ListStatus].TryGetValue(entry.Kind, out var count))
            {
                _counts[entry.ListStatus][entry.Kind] = count - 1;
            }
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
        foreach (var countBucket in _counts.Values)
        {
            countBucket.Clear();
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not AnimeEntity item) return;

        if (string.IsNullOrEmpty(e.PropertyName) || AffectsProjection(e.PropertyName))
        {
            Add(item);
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
            or nameof(AnimeEntity.MediaKind);
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

    private static bool ComputeIsNsfw(AnimeEntity item)
    {
        return string.Equals(item.Rating, "rx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Nsfw, "black", StringComparison.OrdinalIgnoreCase)
            || item.Genres.Any(g => string.Equals(g, "Hentai", StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.ToUpperInvariant();
    }

    private sealed record Entry(
        AnimeEntity Item,
        UserAnimeStatus ListStatus,
        string SearchableText,
        bool IsNsfw,
        MediaKind Kind)
    {
        public static Entry From(AnimeEntity item)
        {
            return new Entry(item, GetListStatus(item), BuildSearchableText(item), ComputeIsNsfw(item), item.MediaKind);
        }
    }
}
