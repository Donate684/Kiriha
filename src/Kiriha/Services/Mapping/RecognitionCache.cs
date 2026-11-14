using System.Collections.Concurrent;
using System.Collections.Immutable;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Utils.Parsing;

namespace Kiriha.Services.Data.Mapping;

public class RecognitionCache : IRecognitionCache
{
    private readonly ConcurrentDictionary<string, ImmutableArray<WeightedMatch>> _indexCache = new();
    private readonly ConcurrentDictionary<string, ImmutableArray<WeightedMatch>> _learnedCache = new();

    public bool IsIndexEmpty => _indexCache.IsEmpty;

    public void BuildIndex(IEnumerable<AnimeEntity> collection)
    {
        _indexCache.Clear();
        foreach (var anime in collection)
        {
            if (anime.MediaKind != MediaKind.Anime)
                continue;

            // Index Title, EnglishTitle, RussianTitle with weight 1.0
            IndexTitle(_indexCache, anime.Title, anime.Id, 1.0f);
            IndexTitle(_indexCache, anime.EnglishTitle, anime.Id, 1.0f);
            IndexTitle(_indexCache, anime.RussianTitle, anime.Id, 1.0f);

            // Index Synonyms with weight 0.5
            if (anime.AlternativeTitles != null)
            {
                foreach (var alt in anime.AlternativeTitles)
                {
                    IndexTitle(_indexCache, alt, anime.Id, 0.5f);
                }
            }

            // Index Title (Year) for year-disambiguated releases and remakes
            int? year = anime.StartYear ?? anime.AiringDate?.Year;
            if (year.HasValue)
            {
                string yearStr = year.Value.ToString();
                if (!string.IsNullOrEmpty(anime.Title) && !anime.Title.Contains(yearStr, StringComparison.Ordinal))
                    IndexTitle(_indexCache, $"{anime.Title} ({year.Value})", anime.Id, 0.9f);
                if (!string.IsNullOrEmpty(anime.EnglishTitle) && !anime.EnglishTitle.Contains(yearStr, StringComparison.Ordinal))
                    IndexTitle(_indexCache, $"{anime.EnglishTitle} ({year.Value})", anime.Id, 0.9f);
                if (!string.IsNullOrEmpty(anime.RussianTitle) && !anime.RussianTitle.Contains(yearStr, StringComparison.Ordinal))
                    IndexTitle(_indexCache, $"{anime.RussianTitle} ({year.Value})", anime.Id, 0.9f);

                if (anime.AlternativeTitles != null)
                {
                    foreach (var alt in anime.AlternativeTitles)
                    {
                        if (!string.IsNullOrWhiteSpace(alt) && !alt.Contains(yearStr, StringComparison.Ordinal))
                            IndexTitle(_indexCache, $"{alt} ({year.Value})", anime.Id, 0.5f);
                    }
                }
            }
        }
    }

    private static void IndexTitle(
        ConcurrentDictionary<string, ImmutableArray<WeightedMatch>> target,
        string? title,
        int id,
        float weight)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        string norm = AnimeStringHelper.Normalize(title);
        if (string.IsNullOrWhiteSpace(norm)) return;

        UpsertMatch(target, norm, id, weight);
    }

    private static void UpsertMatch(
        ConcurrentDictionary<string, ImmutableArray<WeightedMatch>> target,
        string normalizedTitle,
        int id,
        float weight)
    {
        target.AddOrUpdate(
            normalizedTitle,
            static (_, arg) => ImmutableArray.Create(new WeightedMatch(arg.id, arg.weight)),
            static (_, existing, arg) =>
            {
                for (int i = 0; i < existing.Length; i++)
                {
                    if (existing[i].Id == arg.id)
                    {
                        if (existing[i].Weight < arg.weight)
                        {
                            return existing.SetItem(i, new WeightedMatch(arg.id, arg.weight));
                        }
                        return existing;
                    }
                }
                return existing.Add(new WeightedMatch(arg.id, arg.weight));
            },
            (id, weight));
    }

    public IEnumerable<WeightedMatch> Search(string normalizedTitle)
    {
        bool hasIndexed = _indexCache.TryGetValue(normalizedTitle, out var indexed);
        bool hasLearned = _learnedCache.TryGetValue(normalizedTitle, out var learned);

        if (hasIndexed && !hasLearned) return indexed;
        if (!hasIndexed && hasLearned) return learned;
        if (!hasIndexed) return [];

        var merged = new List<WeightedMatch>(indexed.Length + learned.Length);
        merged.AddRange(indexed);
        for (int i = 0; i < learned.Length; i++)
        {
            var l = learned[i];
            int existingIdx = merged.FindIndex(x => x.Id == l.Id);
            if (existingIdx >= 0)
            {
                if (merged[existingIdx].Weight < l.Weight)
                    merged[existingIdx] = l;
            }
            else
            {
                merged.Add(l);
            }
        }
        return merged;
    }

    public void Clear()
    {
        _indexCache.Clear();
        _learnedCache.Clear();
    }

    public void ClearLearnedMatches() => _learnedCache.Clear();

    public void AddMatch(string normalizedTitle, int id, float weight)
    {
        if (string.IsNullOrWhiteSpace(normalizedTitle)) return;
        UpsertMatch(_learnedCache, normalizedTitle, id, weight);
    }
}
