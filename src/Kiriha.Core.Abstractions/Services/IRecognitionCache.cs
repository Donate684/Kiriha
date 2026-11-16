using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

public record struct WeightedMatch(int Id, float Weight);

public interface IRecognitionCache
{
    bool IsIndexEmpty => false;
    void BuildIndex(IEnumerable<AnimeEntity> collection);
    IEnumerable<WeightedMatch> Search(string query);
    void Clear() { }
    void ClearLearnedMatches() { }
    void AddMatch(string normalizedTitle, int id, float weight) { }
}
