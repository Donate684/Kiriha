using System;
using Kiriha.Core.Tracking.Api;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Kiriha.Core.Tracking.Api;

public sealed class ShikiHostResolver
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
