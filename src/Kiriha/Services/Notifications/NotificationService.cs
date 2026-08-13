using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data.Settings;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Notifications;
using Serilog;
using Kiriha.Core.Abstractions.Services;

namespace Kiriha.Services;

/// <summary>
/// Surfaces user-facing notifications via Windows toast (Action Center).
/// Designed to work even when the app is hidden in the tray. Failures are
/// logged but never thrown — notifications are best-effort UX, not critical path.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly ISettingsService _settingsService;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;

    // De-dup: don't fire the same "new episode N for anime X" toast twice in a row.
    // Keyed by anime id, value = the last EpisodesAired count we notified for.
    private readonly ConcurrentDictionary<int, int> _lastNotifiedEpisode = new();

    // De-dup: don't fire the same "new app version" toast twice in a row.
    private string? _lastNotifiedVersion;

    public NotificationService(ISettingsService settingsService, IBackgroundTaskSupervisor backgroundTasks)
    {
        _settingsService = settingsService;
        _backgroundTasks = backgroundTasks;
        AppUserModelIdRegistrar.Register();
    }

    public virtual void NotifyNewEpisode(AnimeEntity anime, int episodeNumber)
    {
        if (anime == null) return;
        if (!_settingsService.Current.System.NotifyNewEpisodes) return;
        if (episodeNumber <= 0) return;

        // Dedupe — only fire when episode number actually advanced for this anime.
        if (_lastNotifiedEpisode.TryGetValue(anime.Id, out var prev) && prev >= episodeNumber)
            return;
        _lastNotifiedEpisode[anime.Id] = episodeNumber;

        // Build a 2- or 3-line toast: bold original title, optional Russian title, then
        // the episode availability line. "Original" is whichever non-Russian title we have
        // (Title is the user's preferred MAL display title — usually English/romaji).
        var orig = !string.IsNullOrEmpty(anime.Title) ? anime.Title : anime.RussianTitle ?? "Anime";
        var ru = anime.RussianTitle;
        var episodeLine = UIUtils.GetLoc("notifications.new_episode.body", episodeNumber);

        // Order: episode line on top (bold by template), then English title, then Russian.
        var lines = new System.Collections.Generic.List<string> { episodeLine, orig };
        if (!string.IsNullOrEmpty(ru) && !string.Equals(ru, orig, StringComparison.Ordinal))
            lines.Add(ru!);

        // Snapshot the delay at the moment of detection. If the user changes it later
        // mid-wait we keep the original behaviour for already-queued notifications.
        var delayMinutes = Math.Max(0, _settingsService.Current.System.NewEpisodeNotificationDelayMinutes);

        if (delayMinutes == 0)
        {
            Log.Information("NotificationService: New episode toast for {Title} ep {Ep}", orig, episodeNumber);
            ToastRenderer.Show(lines);
            return;
        }

        Log.Information("NotificationService: Scheduling new episode toast for {Title} ep {Ep} in {Min} min",
            orig, episodeNumber, delayMinutes);

        _backgroundTasks.Run("NotificationService.DelayedToast", async ct =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(delayMinutes), ct);
                ToastRenderer.Show(lines);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Warning(ex, "NotificationService: delayed toast failed");
            }
        });
    }

    /// <summary>
    /// Fired by the scrobbler when the playing episode is ahead of the user's
    /// progress by more than one episode and <c>NotifyOnSkippedEpisode</c> is on.
    /// Surfaces a toast so the user knows progress was NOT updated.
    /// </summary>
    public virtual void NotifyScrobbleSkipped(AnimeEntity anime, int detectedEp)
    {
        if (anime == null) return;

        var orig = !string.IsNullOrEmpty(anime.Title) ? anime.Title : anime.RussianTitle ?? "Anime";
        var ru = anime.RussianTitle;

        var title = UIUtils.GetLoc("scrobbler.skip_notify.title");
        var body = UIUtils.GetLoc("scrobbler.skip_notify.body", detectedEp, anime.Progress + 1);

        var lines = new System.Collections.Generic.List<string> { title, body, orig };
        if (!string.IsNullOrEmpty(ru) && !string.Equals(ru, orig, StringComparison.Ordinal))
            lines.Add(ru!);

        Log.Information("NotificationService: Scrobble-skip toast for {Title} ep {Ep} (expected {Expected})",
            orig, detectedEp, anime.Progress + 1);
        ToastRenderer.Show(lines);
    }

    public void NotifyAppUpdate(string newVersion)
    {
        if (string.IsNullOrEmpty(newVersion)) return;
        if (!_settingsService.Current.System.NotifyAppUpdate) return;
        if (_lastNotifiedVersion == newVersion) return;
        _lastNotifiedVersion = newVersion;

        var title = UIUtils.GetLoc("notifications.app_update.title");
        var body = UIUtils.GetLoc("notifications.app_update.body", newVersion);

        Log.Information("NotificationService: Update toast for version {Version}", newVersion);
        ToastRenderer.Show(new System.Collections.Generic.List<string> { title, body });
    }
}
