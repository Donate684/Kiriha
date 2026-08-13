using System;
using System.Collections.Generic;

namespace Kiriha.Core.Tracking.Api;

public sealed class ShikiProbeStrategy
{
    private readonly ShikiHostState _state;

    public static readonly IReadOnlyList<string> KnownOriginalHosts = new[]
    {
        "shikimori.one",
        "shikimori.io"
    };

    public static readonly IReadOnlyList<string> KnownForkHosts = new[]
    {
        "shikimori.rip",
        "shikimori.net",
        "shikimori.fi",
    };

    public ShikiProbeStrategy(ShikiHostState state)
    {
        _state = state;
    }

    public IEnumerable<string> ProbeOrder(string excluding)
    {
        IReadOnlyList<string> knownHosts;

        if (_state.IsOriginalHost(excluding)) knownHosts = KnownOriginalHosts;
        else if (_state.IsForkHost(excluding)) knownHosts = KnownForkHosts;
        else yield break;

        foreach (var host in knownHosts)
        {
            if (!string.Equals(host, excluding, StringComparison.OrdinalIgnoreCase))
                yield return host;
        }
    }
}
