using System.Collections.Generic;
using Kiriha.Models.Entities;

namespace Kiriha.Core.Services;

public record struct WeightedMatch(int Id, float Weight);

public interface IRecognitionCache
{
    void BuildIndex(IEnumerable<AnimeEntity> collection);
    IEnumerable<WeightedMatch> Search(string query);
}