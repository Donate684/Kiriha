namespace Kiriha.Core.Domain.Models.Entities;

/// <summary>The user-editable fields of an anime or manga library entry.</summary>
public readonly record struct AnimeEditFields(
    UserAnimeStatus Status, int Progress, int ChaptersRead, int VolumesRead, string Score,
    string? Notes, bool IsRewatching, int RewatchCount, DateTime? DateStarted, DateTime? DateCompleted)
{
    public static AnimeEditFields From(AnimeEntity item) => new(
        item.Status, item.MediaKind == MediaKind.Anime ? item.Progress : 0,
        item.MediaKind == MediaKind.Anime ? 0 : item.ChaptersRead,
        item.MediaKind == MediaKind.Anime ? 0 : item.VolumesRead,
        item.Score, item.Notes, item.IsRewatching, item.RewatchCount, item.DateStarted, item.DateCompleted);

    public bool TryMerge(AnimeEditFields draft, AnimeEditFields latest, out AnimeEditFields merged)
    {
        bool conflict = false;
        merged = new AnimeEditFields(
            Merge(Status, draft.Status, latest.Status),
            Merge(Progress, draft.Progress, latest.Progress),
            Merge(ChaptersRead, draft.ChaptersRead, latest.ChaptersRead),
            Merge(VolumesRead, draft.VolumesRead, latest.VolumesRead),
            Merge(Score, draft.Score, latest.Score),
            Merge(Notes, draft.Notes, latest.Notes),
            Merge(IsRewatching, draft.IsRewatching, latest.IsRewatching),
            Merge(RewatchCount, draft.RewatchCount, latest.RewatchCount),
            Merge(DateStarted, draft.DateStarted, latest.DateStarted),
            Merge(DateCompleted, draft.DateCompleted, latest.DateCompleted));
        return !conflict;

        T Merge<T>(T baseline, T proposed, T current)
        {
            var comparer = EqualityComparer<T>.Default;
            if (comparer.Equals(proposed, baseline))
                return current;

            if (!comparer.Equals(current, baseline) && !comparer.Equals(current, proposed))
                conflict = true;

            return proposed;
        }
    }

    public void ApplyTo(AnimeEntity item)
    {
        item.Status = Status;
        if (item.MediaKind == MediaKind.Anime)
            item.Progress = Progress;
        else
        {
            item.ChaptersRead = ChaptersRead;
            item.VolumesRead = VolumesRead;
        }
        item.Score = Score;
        item.Notes = Notes;
        item.IsRewatching = IsRewatching;
        item.RewatchCount = RewatchCount;
        item.DateStarted = DateStarted;
        item.DateCompleted = DateCompleted;
    }
}
