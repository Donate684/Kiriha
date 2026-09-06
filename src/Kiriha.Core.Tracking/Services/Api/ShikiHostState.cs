using System;
using Kiriha.Core.Tracking.Api;
using System.Collections.Generic;
using System.Threading;

namespace Kiriha.Core.Tracking.Api;

public sealed class ShikiHostState
{
    private readonly Lock _gate = new();

    private readonly HashSet<string> _originalHosts;
    private readonly HashSet<string> _forkHosts;

    private string? _activeOriginalHost;
    private string? _activeForkHost;

    public ShikiHostState(IReadOnlyList<string> knownOriginalHosts, IReadOnlyList<string> knownForkHosts)
    {
        _originalHosts = new(knownOriginalHosts, StringComparer.OrdinalIgnoreCase);
        _forkHosts = new(knownForkHosts, StringComparer.OrdinalIgnoreCase);
    }

    public string? ActiveOriginalHost
    {
        get { lock (_gate) return _activeOriginalHost; }
    }

    public string? ActiveForkHost
    {
        get { lock (_gate) return _activeForkHost; }
    }

    public Uri Rewrite(Uri original)
    {
        lock (_gate)
        {
            if (_originalHosts.Contains(original.Host))
            {
                if (_activeOriginalHost != null && !string.Equals(_activeOriginalHost, original.Host, StringComparison.OrdinalIgnoreCase))
                {
                    return new UriBuilder(original) { Host = _activeOriginalHost }.Uri;
                }
            }
            else if (_forkHosts.Contains(original.Host))
            {
                if (_activeForkHost != null && !string.Equals(_activeForkHost, original.Host, StringComparison.OrdinalIgnoreCase))
                {
                    return new UriBuilder(original) { Host = _activeForkHost }.Uri;
                }
            }
        }
        return original;
    }

    public bool Remember(string fromHost, string toHost)
    {
        lock (_gate)
        {
            if (!IsSameRealmInternal(fromHost, toHost)) return false;

            if (_originalHosts.Contains(fromHost))
            {
                _originalHosts.Add(toHost);
                _activeOriginalHost = toHost;
                return true;
            }

            if (_forkHosts.Contains(fromHost))
            {
                _forkHosts.Add(toHost);
                _activeForkHost = toHost;
                return true;
            }
        }
        return false;
    }

    public void Reset()
    {
        lock (_gate)
        {
            _activeOriginalHost = null;
            _activeForkHost = null;
        }
    }

    public bool IsOriginalHost(string host)
    {
        lock (_gate) return _originalHosts.Contains(host);
    }

    public bool IsForkHost(string host)
    {
        lock (_gate) return _forkHosts.Contains(host);
    }

    public bool IsKnownHost(string host)
    {
        lock (_gate) return _originalHosts.Contains(host) || _forkHosts.Contains(host);
    }

    public bool IsSameRealm(string a, string b)
    {
        lock (_gate) return IsSameRealmInternal(a, b);
    }

    private bool IsSameRealmInternal(string a, string b)
    {
        bool aIsOriginal = _originalHosts.Contains(a);
        bool aIsFork = _forkHosts.Contains(a);

        bool bIsOriginal = _originalHosts.Contains(b);
        bool bIsFork = _forkHosts.Contains(b);

        if (aIsOriginal && bIsOriginal) return true;
        if (aIsFork && bIsFork) return true;

        if (aIsOriginal && !bIsFork && ShikiHostResolver.IsShikiHost(b)) return true;
        if (aIsFork && !bIsOriginal && ShikiHostResolver.IsShikiHost(b)) return true;

        return false;
    }
}
