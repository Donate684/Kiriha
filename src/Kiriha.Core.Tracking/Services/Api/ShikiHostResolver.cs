using System.Text.RegularExpressions;
using Kiriha.Core.Abstractions.Services;

namespace Kiriha.Core.Tracking.Api;

public sealed class ShikiHostResolver : IImageUrlRewriter
{
    private static readonly Regex ShikiHostPattern =
        new(@"^shikimori\.[a-z]{2,6}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ShikiHostState _state;
    private readonly ShikiProbeStrategy _probeStrategy;

    public ShikiHostResolver()
    {
        _state = new ShikiHostState(ShikiProbeStrategy.KnownOriginalHosts, ShikiProbeStrategy.KnownForkHosts);
        _probeStrategy = new ShikiProbeStrategy(_state);
    }

    public static bool IsShikiHost(string host) => ShikiHostPattern.IsMatch(host);

    /// <inheritdoc />
    /// <remarks>
    /// Only rewrites URLs whose host is a known Shikimori host and for which a
    /// session-pinned replacement exists. All other URLs are returned unchanged.
    /// </remarks>
    public string Rewrite(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        if (!_state.IsKnownHost(uri.Host)) return url;

        var rewritten = _state.Rewrite(uri);
        return ReferenceEquals(rewritten, uri) ? url : rewritten.ToString();
    }

    public Uri Rewrite(Uri original) => _state.Rewrite(original);
    
    public bool Remember(string fromHost, string toHost) => _state.Remember(fromHost, toHost);
    
    public void Reset() => _state.Reset();

    public string? ActiveForkHost => _state.ActiveForkHost;
    
    public string? ActiveOriginalHost => _state.ActiveOriginalHost;

    public bool IsOriginalHost(string host) => _state.IsOriginalHost(host);
    
    public bool IsForkHost(string host) => _state.IsForkHost(host);
    
    public bool IsKnownHost(string host) => _state.IsKnownHost(host);
    
    public bool IsSameRealm(string a, string b) => _state.IsSameRealm(a, b);

    public IEnumerable<string> ProbeOrder(string excluding) => _probeStrategy.ProbeOrder(excluding);
}
