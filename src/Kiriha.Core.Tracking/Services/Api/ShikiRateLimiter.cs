using System;
using System.Threading.RateLimiting;
using System.Threading;
using System.Threading.Tasks;

namespace Kiriha.Core.Tracking.Api;

/// <summary>
/// Shikimori rate limit: 5 RPS. We pace at 250 ms between requests
/// (≈4 RPS) to leave headroom for transient bursts.
/// </summary>
public class ShikiRateLimiter : IDisposable
{
    private readonly SemaphoreSlim _rateLimitLock = new(1, 1);
    private DateTime _lastRequest = DateTime.MinValue;
    private readonly TimeSpan _minInterval = TimeSpan.FromMilliseconds(250);

    public async Task ThrottleAsync(CancellationToken ct = default)
    {
        await _rateLimitLock.WaitAsync(ct);
        try
        {
            var elapsed = DateTime.UtcNow - _lastRequest;
            if (elapsed < _minInterval)
                await Task.Delay(_minInterval - elapsed, ct);
            _lastRequest = DateTime.UtcNow;
        }
        finally
        {
            _rateLimitLock.Release();
        }
    }

    public void Dispose()
    {
        _rateLimitLock.Dispose();
    }
}
