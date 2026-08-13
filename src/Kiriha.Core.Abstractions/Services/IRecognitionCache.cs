using System.Collections.Generic;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

public record struct WeightedMatch(int Id, float Weight);

public interface IRecognitionCache
{
    void BuildIndex(IEnumerable<AnimeEntity> collection);
    IEnumerable<WeightedMatch> Search(string query);
}
