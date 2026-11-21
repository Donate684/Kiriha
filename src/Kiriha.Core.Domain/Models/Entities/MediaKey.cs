namespace Kiriha.Core.Domain.Models.Entities;

/// <summary>
/// Canonical composite key uniquely identifying a library media item across Anime, Manga, and Light Novels.
/// Prevents primary-key and index collisions when an anime and a manga share the same numeric tracker ID.
/// </summary>
public readonly record struct MediaKey(int Id, MediaKind Kind)
{
    public override string ToString() => $"{Kind}:{Id}";
}

/// <summary>Identifies one membership of a title in the local library.</summary>
public readonly record struct LibraryEntryTarget(MediaKey Key, long Generation);
