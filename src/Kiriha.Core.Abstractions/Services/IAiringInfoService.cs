using System;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

public interface IAiringInfoService
{
    Task SyncEpisodesForAnimeAsync(AnimeEntity anime, CancellationToken ct = default);
    Task SyncOngoingEpisodesAsync(bool force = false, IProgress<string>? progress = null, CancellationToken ct = default);
}
