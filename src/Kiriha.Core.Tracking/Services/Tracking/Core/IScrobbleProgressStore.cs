namespace Kiriha.Core.Tracking.Core;

public interface IScrobbleProgressStore
{
    int GetElapsed(string key);
    void SaveElapsed(string key, int elapsed);
    void Clear(string key);
}