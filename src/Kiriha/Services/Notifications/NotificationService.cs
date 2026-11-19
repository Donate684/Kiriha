using System.Collections.Concurrent;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Metadata;
using Kiriha.Services.Notifications;
using Serilog;

namespace Kiriha.Services;

/// <summary>
/// Surfaces user-facing notifications via Windows toast (Action Center).
/// Designed to work even when the app is hidden in the tray. Failures are
/// logged but never thrown — notifications are best-effort UX, not critical path.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly ISettingsService _settingsService;
    private readonly ILocalizer _localizer;

    // De-dup: don't fire the same "new episode N for anime X" toast twice in a row.
    // Keyed by anime id, value = the last EpisodesAired count we notified for.
    private readonly ConcurrentDictionary<int, int> _lastNotifiedEpisode = new();

    // De-dup: don't fire the same "new app version" toast twice in a row.
    private string? _lastNotifiedVersion;

    public NotificationService(ISettingsService settingsService, ILocalizer? localizer = null)
    {
        _settingsService = settingsService;
        _localizer = localizer ?? LocalizationService.Active;
    }

    public virtual void NotifyNewEpisode(AnimeEntity anime, int episode)
    {
        if (anime is null) return;
        if (!_settingsService.Current.System.NotifyNewEpisodes) return;
        if (episode <= 0) return;

        // Dedupe — only fire when episode number actually advanced for this anime.
        if (_lastNotifiedEpisode.TryGetValue(anime.Id, out var prev) && prev >= episode)
            return;
        _lastNotifiedEpisode[anime.Id] = episode;

        var orig = !string.IsNullOrEmpty(anime.Title) ? anime.Title : anime.RussianTitle ?? "Anime";
        var ru = anime.RussianTitle;
        var episodeLine = _localizer.GetLoc("notifications.new_episode.body", episode);

        var lines = new List<string> { episodeLine, orig };
        if (!string.IsNullOrEmpty(ru) && !string.Equals(ru, orig, StringComparison.Ordinal))
            lines.Add(ru);

        Log.Information("NotificationService: New episode toast for {Title} ep {Ep}", orig, episode);
        ToastRenderer.Show(lines, anime.PosterSource);
    }

    public virtual void NotifyScrobbleSkipped(AnimeEntity anime, int episode)
    {
        if (anime is null) return;

        var displayTitle = !string.IsNullOrWhiteSpace(anime.Presentation.DisplayTitle)
            ? anime.Presentation.DisplayTitle
            : "Anime";

        var title = _localizer.GetLoc("scrobbler.skip_notify.title");
        var body = _localizer.GetLoc("scrobbler.skip_notify.body", episode, anime.Progress + 1);

        var lines = new List<string> { title, displayTitle, body };

        Log.Information("NotificationService: Scrobble-skip toast for {Title} ep {Ep} (expected {Expected})",
            displayTitle, episode, anime.Progress + 1);
        ToastRenderer.Show(lines, anime.LocalPosterPath ?? anime.PosterSource);
    }

    public virtual void NotifyAnimeCompleted(AnimeEntity anime)
    {
        if (anime is null) return;

        var displayTitle = !string.IsNullOrWhiteSpace(anime.Presentation.DisplayTitle)
            ? anime.Presentation.DisplayTitle
            : "Anime";

        var title = _localizer.GetLoc("scrobbler.quick_rating.title");
        var body = _localizer.GetLoc("scrobbler.quick_rating.subtitle");

        var lines = new List<string> { title, displayTitle, body };

        Log.Information("NotificationService: Anime completed toast for {Title}", displayTitle);
        ToastRenderer.Show(lines, anime.LocalPosterPath ?? anime.PosterSource);
    }

    public void NotifyAppUpdate(string newVersion)
    {
        if (string.IsNullOrEmpty(newVersion)) return;
        if (!_settingsService.Current.System.NotifyAppUpdate) return;
        if (_lastNotifiedVersion == newVersion) return;
        _lastNotifiedVersion = newVersion;

        var title = _localizer.GetLoc("notifications.app_update.title");
        var body = _localizer.GetLoc("notifications.app_update.body", newVersion);

        Log.Information("NotificationService: Update toast for version {Version}", newVersion);
        ToastRenderer.Show([title, body]);
    }

    public virtual void NotifySyncFailed(string animeTitle, string trackerName, string errorMessage, bool willRetry)
    {
        var title = _localizer.GetLoc("notifications.sync_failed.title", trackerName);
        var body = willRetry
            ? _localizer.GetLoc("notifications.sync_failed.retry_body", trackerName, animeTitle)
            : _localizer.GetLoc("notifications.sync_failed.permanent_body", trackerName, animeTitle);

        var lines = new List<string> { title, body, animeTitle };

        Log.Warning("NotificationService: Sync failed toast for {Anime} on {Tracker} (willRetry={WillRetry}): {Error}",
            animeTitle, trackerName, willRetry, errorMessage);
        ToastRenderer.Show(lines);
    }
}
