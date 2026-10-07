using System.Globalization;
using Avalonia.Data.Converters;
using Kiriha.Core;
using Kiriha.ViewModels.History;

namespace Kiriha.Views.Converters;

public class HistoryActionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int typeId;
        bool isZeroProgress = false;

        if (value is HistoryEntryVm entry)
        {
            typeId = entry.ActionType;
            isZeroProgress = entry.EpisodeFrom == 0 && entry.EpisodeTo == 0;
        }
        else if (value is int id)
        {
            typeId = id;
        }
        else
        {
            return value;
        }

        string? param = parameter?.ToString();

        if (param == "icon")
        {
            if (isZeroProgress && (typeId == 1 || typeId == 0 || typeId == 12))
            {
                return "PlaylistPlus";
            }

            return typeId switch
            {
                1 => "CheckCircleOutline",   // Watched / Watching
                2 => "History",              // Reverted
                3 => "AlertCircleOutline",   // SyncFailed
                4 => "Broadcast",            // Scrobbled
                5 => "StarOutline",          // ScoreSet
                6 => "TrophyOutline",         // Completed
                7 => "CloseCircleOutline",    // Dropped
                8 => "DeleteOutline",         // Deleted
                9 => "BookmarkOutline",       // PlanToWatch
                10 => "PauseCircleOutline",   // OnHold
                11 => "Repeat",               // Rewatching
                12 => "PlaylistPlus",         // AddedToList
                _ => "CheckCircleOutline"
            };
        }

        // If progress is 0 for watched/watching/unclassified, show "Added to list"
        if (isZeroProgress && (typeId == 1 || typeId == 0 || typeId == 12))
        {
            return UIUtils.GetLoc("history.actions.added_to_list");
        }

        // Text localization
        string key = typeId switch
        {
            1 => "history.actions.watched",
            2 => "history.actions.reverted",
            3 => "history.actions.sync_failed",
            4 => "history.actions.scrobbled",
            5 => "history.actions.score_set",
            6 => "history.actions.completed",
            7 => "history.actions.dropped",
            8 => "history.actions.deleted",
            9 => "history.actions.plan_to_watch",
            10 => "history.actions.on_hold",
            11 => "history.actions.rewatching",
            12 => "history.actions.added_to_list",
            _ => "history.actions.watched"
        };

        return UIUtils.GetLoc(key);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}
