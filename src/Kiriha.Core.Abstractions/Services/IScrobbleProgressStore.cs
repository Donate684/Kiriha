namespace Kiriha.Core.Abstractions.Services;

public interface IScrobbleProgressStore
{
    int GetElapsed(string key);
    void SaveElapsed(string key, int elapsed);
    void Clear(string key);
    Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
}
