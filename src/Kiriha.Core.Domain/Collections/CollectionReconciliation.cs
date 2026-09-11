using System;
using System.Collections.Generic;
using System.Linq;

namespace Kiriha.Core.Domain.Collections;

/// <summary>
/// Result of reconciling two collections (e.g. local database vs remote tracking service).
/// </summary>
public record class ReconciliationResult<TLocal, TRemote>(
    List<TLocal> LocalOnly,
    List<TRemote> RemoteOnly,
    List<(TLocal Local, TRemote Remote)> Matched);

/// <summary>
/// High-performance reconciliation utilities powered by .NET 11 LINQ FullJoin.
/// </summary>
public static class CollectionReconciliation
{
    /// <summary>
    /// Performs a single-pass 3-way reconciliation of local and remote items using .NET 11 LINQ FullJoin.
    /// </summary>
    public static ReconciliationResult<TLocal, TRemote> Reconcile<TLocal, TRemote, TKey>(
        IEnumerable<TLocal> localItems,
        IEnumerable<TRemote> remoteItems,
        Func<TLocal, TKey> localKeySelector,
        Func<TRemote, TKey> remoteKeySelector,
        IEqualityComparer<TKey>? keyComparer = null)
        where TLocal : class
        where TRemote : class
    {
        var localOnly = new List<TLocal>();
        var remoteOnly = new List<TRemote>();
        var matched = new List<(TLocal Local, TRemote Remote)>();

        foreach (var (local, remote) in localItems.FullJoin(remoteItems, localKeySelector, remoteKeySelector, keyComparer))
        {
            if (local != null && remote != null)
            {
                matched.Add((local, remote));
            }
            else if (local != null)
            {
                localOnly.Add(local);
            }
            else if (remote != null)
            {
                remoteOnly.Add(remote);
            }
        }

        return new ReconciliationResult<TLocal, TRemote>(localOnly, remoteOnly, matched);
    }
}
