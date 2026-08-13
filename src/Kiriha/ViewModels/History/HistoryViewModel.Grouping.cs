using Kiriha.Core.Domain.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using Kiriha.Core;

namespace Kiriha.ViewModels.History;

public partial class HistoryViewModel
{
    private void ApplyFilters()
    {
        var filtered = _rawItems.AsEnumerable();

        // Period
        var now = DateTime.UtcNow;
        filtered = SelectedPeriod switch
        {
            1 => filtered.Where(x => x.Timestamp.Date == now.Date),
            2 => filtered.Where(x => x.Timestamp >= now.AddDays(-7)),
            3 => filtered.Where(x => x.Timestamp >= now.AddDays(-30)),
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
        var newGroups = new List<HistoryGroup>();
        foreach (var dateGroup in list.GroupBy(x => x.Timestamp.Date).OrderByDescending(g => g.Key))
        {
            var group = new HistoryGroup { Header = GetFriendlyDate(dateGroup.Key) };
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
                    if (run != null) group.Items.Add(run);
                    run = new HistoryEntryVm
                    {
                        AnimeId = item.AnimeId,
                        AnimeTitle = item.AnimeTitle,
                        RussianTitle = item.RussianTitle,
                        PosterUrl = item.PosterUrl,
                        ActionType = item.ActionType,
                        Detail = item.Detail,
                        Timestamp = item.Timestamp,
                        EpisodeFrom = item.Episode,
                        EpisodeTo = item.Episode,
                        Primary = item
                    };
                }
            }
            if (run != null) group.Items.Add(run);
            if (group.Items.Count > 0) newGroups.Add(group);
        }

        GroupedHistory.Clear();
        foreach (var g in newGroups) GroupedHistory.Add(g);
        HasResults = newGroups.Count > 0;
    }

    private string GetFriendlyDate(DateTime date)
    {
        var now = DateTime.UtcNow.Date;
        if (date == now) return UIUtils.GetLoc("common.time.today");
        if (date == now.AddDays(-1)) return UIUtils.GetLoc("common.time.yesterday");

        var culture = _settings.Current.UI.LanguageCode == AppConstants.Languages.Ru ? new System.Globalization.CultureInfo("ru-RU") : new System.Globalization.CultureInfo("en-US");
        return date.ToString("d MMMM", culture);
    }
}
