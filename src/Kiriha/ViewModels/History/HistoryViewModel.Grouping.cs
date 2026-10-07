namespace Kiriha.ViewModels.History;

public partial class HistoryViewModel
{
    private void ApplyFilters()
    {
        var filtered = _rawItems.AsEnumerable();

        // Period
        var now = DateTime.Now;
        filtered = SelectedPeriod switch
        {
            1 => filtered.Where(x => x.Timestamp.ToLocalTime().Date == now.Date),
            2 => filtered.Where(x => x.Timestamp.ToLocalTime() >= now.AddDays(-7)),
            3 => filtered.Where(x => x.Timestamp.ToLocalTime() >= now.AddDays(-30)),
            _ => filtered
        };

        // Action
        if (SelectedAction == 1)
            filtered = filtered.Where(x => x.ActionType == 1 || x.ActionType == 4);
        else if (SelectedAction != 0)
            filtered = filtered.Where(x => x.ActionType == SelectedAction);

        // Search
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var q = SearchQuery.Trim();
            filtered = filtered.Where(x =>
                (x.AnimeTitle?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (x.RussianTitle?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var list = filtered.OrderByDescending(x => x.Timestamp).ToList();

        // Build groups by date, merging consecutive same-anime watch episodes.
        var timeline = new List<HistoryTimelineItem>();
        bool isFirstGroup = true;
        var orderedTrackers = GetOrderedActiveTrackers();

        foreach (var dateGroup in list.GroupBy(x => x.Timestamp.ToLocalTime().Date).OrderByDescending(g => g.Key))
        {
            var header = GetFriendlyDate(dateGroup.Key);
            var groupEntries = new List<HistoryEntryVm>();
            HistoryEntryVm? run = null;

            foreach (var item in dateGroup) // already desc by timestamp
            {
                bool canMerge =
                    run != null &&
                    run.AnimeId == item.AnimeId &&
                    run.ActionType == item.ActionType &&
                    (item.ActionType == 1 || item.ActionType == 4 || item.ActionType == 6) &&
                    item.Episode > 0 &&
                    item.Episode == run.EpisodeFrom - 1;

                if (canMerge)
                {
                    run!.EpisodeFrom = item.Episode;
                    run.Count++;
                }
                else
                {
                    if (run != null)
                    {
                        groupEntries.Add(run);
                    }
                    run = new HistoryEntryVm(_localizer)
                    {
                        AnimeId = item.AnimeId,
                        AnimeTitle = item.AnimeTitle,
                        RussianTitle = item.RussianTitle,
                        PosterUrl = item.PosterUrl,
                        ActionType = item.ActionType,
                        Detail = item.Detail,
                        Timestamp = item.Timestamp.ToLocalTime(),
                        EpisodeFrom = item.Episode,
                        EpisodeTo = item.Episode,
                        Primary = item
                    };
                    var currentRun = run;
                    run.LoadTrackerStatuses(
                        item.TrackerStatuses,
                        trackerName => HandleTrackerSyncAsync(currentRun, trackerName),
                        trackerName => IsTrackerEnabled(trackerName),
                        orderedTrackers);
                }

            }
            if (run != null)
            {
                groupEntries.Add(run);
            }

            if (groupEntries.Count > 0)
            {
                timeline.Add(new HistoryDateHeaderItem
                {
                    Header = header,
                    IsFirst = isFirstGroup
                });
                isFirstGroup = false;

                for (int i = 0; i < groupEntries.Count; i++)
                {
                    groupEntries[i].IsFirstInGroup = (i == 0);
                    groupEntries[i].IsLastInGroup = (i == groupEntries.Count - 1);
                    timeline.Add(groupEntries[i]);
                }
            }
        }

        TimelineItems.Clear();
        TimelineItems.AddRange(timeline);

        HasResults = timeline.Count > 0;
    }

    private string GetFriendlyDate(DateTime date)
    {
        var now = DateTime.Today;
        if (date == now) return _localizer.GetLoc("common.time.today");
        if (date == now.AddDays(-1)) return _localizer.GetLoc("common.time.yesterday");

        return date.ToString("d MMMM", _localizer.CurrentCulture);
    }

    private async Task HandleTrackerSyncAsync(HistoryEntryVm entry, string trackerName)
    {
        if (_syncManager == null) return;

        var tracker = _trackers.FirstOrDefault(t =>
            string.Equals(t.Name, trackerName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.TrackerId, trackerName, StringComparison.OrdinalIgnoreCase))
            ?? (_settingsService != null && trackerName.StartsWith("Shiki", StringComparison.OrdinalIgnoreCase)
                ? _trackers.FirstOrDefault(t => t.TrackerId == (_settingsService.Current.Api.ShikiMirror == Kiriha.Core.Domain.Models.ShikiMirror.Net ? Kiriha.Core.Domain.Constants.TrackerConstants.Ids.ShikiFork : Kiriha.Core.Domain.Constants.TrackerConstants.Ids.ShikiOrig))
                : null);

        if (tracker != null && !tracker.IsEnabled)
        {
            _notificationService?.NotifySyncFailed(
                entry.AnimeTitle,
                trackerName,
                _localizer.GetLoc(HistoryEntryVm.NotLoggedInLocKey, trackerName),
                false);
            return;
        }

        int progress = Math.Max(0, entry.EpisodeTo);
        Kiriha.Core.Domain.Models.Entities.UserAnimeStatus? status = entry.ActionType switch
        {
            6 => Kiriha.Core.Domain.Models.Entities.UserAnimeStatus.Completed,
            7 => Kiriha.Core.Domain.Models.Entities.UserAnimeStatus.Dropped,
            _ => null
        };
        int? score = null;
        if (entry.ActionType == 5 && int.TryParse(entry.Detail, out int parsedScore))
        {
            score = parsedScore;
        }

        await _syncManager.EnqueueUpdateAsync(entry.AnimeId, progress, status, score, tracker?.Name ?? trackerName);
    }
}
