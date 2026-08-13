using System;

namespace Kiriha.Core.Services;

public interface IDiscordService
{
    void UpdatePresence(string title, string? episode, int totalEpisodes = 0, string? malUrl = null, string? shikiUrl = null, TimeSpan? position = null, TimeSpan? duration = null, string? imageUrl = null, bool isPlaying = true);
    void ClearPresence();
}
