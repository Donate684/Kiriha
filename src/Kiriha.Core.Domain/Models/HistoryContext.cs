using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Domain.Models;

/// <summary>
/// Ambient execution-flow context carrying <see cref="MediaKind"/> across legacy
/// <c>IHistoryService</c> overloads without altering existing method signatures.
/// </summary>
public static class HistoryContext
{
    private static readonly AsyncLocal<MediaKind?> AmbientKind = new();

    public static MediaKind? CurrentMediaKind => AmbientKind.Value;

    public static Scope UseMediaKind(MediaKind mediaKind)
    {
        var previous = AmbientKind.Value;
        AmbientKind.Value = mediaKind;
        return new Scope(previous);
    }

    public readonly struct Scope(MediaKind? previous) : IDisposable
    {
        public void Dispose() => AmbientKind.Value = previous;
    }
}
