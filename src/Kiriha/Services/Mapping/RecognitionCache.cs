using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Services.Data.Mapping;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Utils.Parsing;

namespace Kiriha.Services.Data.Mapping;

public class RecognitionCache : IRecognitionCache
{
    private readonly ConcurrentDictionary<string, ImmutableArray<WeightedMatch>> _cache = new();

    public void BuildIndex(IEnumerable<AnimeEntity> collection)
    {
        _cache.Clear();
        foreach (var anime in collection)
        {
            // Index Title, EnglishTitle, RussianTitle with weight 1.0
            IndexTitle(anime.Title, anime.Id, 1.0f);
            IndexTitle(anime.EnglishTitle, anime.Id, 1.0f);
            IndexTitle(anime.RussianTitle, anime.Id, 1.0f);

            // Index Synonyms with weight 0.5
            if (anime.AlternativeTitles != null)
            {
                foreach (var alt in anime.AlternativeTitles)
                {
                    IndexTitle(alt, anime.Id, 0.5f);
                }
            }

            // Index Title (Year) with weight 0.5
            if (anime.StartYear.HasValue)
            {
                IndexTitle($"{anime.Title} ({anime.StartYear})", anime.Id, 0.5f);
                if (!string.IsNullOrEmpty(anime.EnglishTitle))
                    IndexTitle($"{anime.EnglishTitle} ({anime.StartYear})", anime.Id, 0.5f);
                if (!string.IsNullOrEmpty(anime.RussianTitle))
                    IndexTitle($"{anime.RussianTitle} ({anime.StartYear})", anime.Id, 0.5f);
            }
        }
    }

    private void IndexTitle(string? title, int id, float weight)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        string norm = AnimeStringHelper.Normalize(title);
        if (string.IsNullOrWhiteSpace(norm)) return;

        _cache.AddOrUpdate(
            norm,
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
        if (_cache.TryGetValue(normalizedTitle, out var matches))
            return matches;
        return [];
    }

    public void Clear() => _cache.Clear();

    public void AddMatch(string normalizedTitle, int id, float weight)
    {
        _cache.AddOrUpdate(
            normalizedTitle,
            static (_, arg) => ImmutableArray.Create(new WeightedMatch(arg.id, arg.weight)),
            static (_, existing, arg) =>
            {
                for (int i = 0; i < existing.Length; i++)
                {
                    if (existing[i].Id == arg.id)
                        return existing;
                }
                return existing.Add(new WeightedMatch(arg.id, arg.weight));
            },
            (id, weight));
    }
}
