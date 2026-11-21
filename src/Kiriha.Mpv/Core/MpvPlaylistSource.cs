namespace Kiriha.Mpv;

/// <summary>Associates libmpv's playlist events with the command that created their entry.</summary>
internal sealed class MpvPlaylistSource
{
    private long _requested;
    private long _submitted;
    private readonly List<(long First, long Last)> _entries = [];
    private long _startedEntry;

    // Reserve can run on the UI thread while a native load command is completing.
    // Registration and event processing are serialized by the caller.
    internal long Reserve() => Interlocked.Increment(ref _requested);

    internal bool IsRequested(long sourceId) => sourceId != 0 && sourceId == Volatile.Read(ref _requested);

    internal void Register(long sourceId, long entryId)
    {
        _submitted = sourceId;
        _entries.Clear();
        _entries.Add((entryId, entryId));
        _startedEntry = 0;
    }

    internal void Start(long entryId)
    {
        _startedEntry = entryId;
    }

    internal long StartedSource
    {
        get
        {
            if (_startedEntry <= 0 || !IsRequested(_submitted))
                return 0;

            foreach (var (first, last) in _entries)
            {
                if (_startedEntry >= first && _startedEntry <= last)
                    return _submitted;
            }
            return 0;
        }
    }

    internal long End(long entryId, long firstInsertedEntry, int insertedCount)
    {
        var sourceId = StartedSource;
        if (entryId != _startedEntry || sourceId == 0)
            return 0;

        _startedEntry = 0;
        if (firstInsertedEntry > 0 && insertedCount > 0)
            _entries.Add((firstInsertedEntry, firstInsertedEntry + insertedCount - 1));

        return sourceId;
    }
}
