using System;
using System.Threading;
using System.Threading.Tasks;

namespace Kiriha.Core.Abstractions.Services.Tracking;

public interface IMalHistoryDeepParserService
{
    Task<(DateTime? StartDate, DateTime? EndDate)?> FetchLatestWatchDatesAsync(int malId, string cookie, bool isManga = false, CancellationToken ct = default);
}
