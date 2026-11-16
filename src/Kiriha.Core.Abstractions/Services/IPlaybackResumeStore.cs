namespace Kiriha.Core.Abstractions.Services;

public interface IPlaybackResumeStore
{
    void SavePosition(string url, double position, double duration);
    double? TryGetResumePosition(string url, string? watchLaterDir = null);
    void Remove(string url);
    Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
}
