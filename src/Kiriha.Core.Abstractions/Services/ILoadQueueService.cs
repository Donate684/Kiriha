using System.Collections.Generic;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

public interface ILoadQueueService
{
    void EnqueueForViewport(IEnumerable<AnimeEntity> items);
    void ClearQueues();
}
