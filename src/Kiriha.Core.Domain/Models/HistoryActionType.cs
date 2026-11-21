namespace Kiriha.Core.Domain.Models;

/// <summary>
/// Strongly-typed history event category persisted in <see cref="HistoryItem.ActionType"/>.
/// Numeric values match the SQLite <c>history.action_type</c> column schema.
/// </summary>
public enum HistoryActionType
{
    Watched = 1,
    Reverted = 2,
    SyncFailed = 3,
    Scrobbled = 4,
    ScoreSet = 5,
    Completed = 6,
    Dropped = 7,
    Deleted = 8,
    PlanToWatch = 9,
    OnHold = 10,
    Rewatching = 11,
    AddedToList = 12,
    Edited = 13
}

public static class HistoryActionTypeExtensions
{
    public static HistoryActionType ParseActionType(string? actionType) => actionType switch
    {
        "Watched" or "Watching" or "Read" or "Reading" => HistoryActionType.Watched,
        "Reverted" => HistoryActionType.Reverted,
        "SyncFailed" => HistoryActionType.SyncFailed,
        "Scrobbled" => HistoryActionType.Scrobbled,
        "ScoreSet" => HistoryActionType.ScoreSet,
        "Completed" => HistoryActionType.Completed,
        "Dropped" => HistoryActionType.Dropped,
        "Deleted" or "Removed" => HistoryActionType.Deleted,
        "PlanToWatch" or "Plan_To_Watch" => HistoryActionType.PlanToWatch,
        "OnHold" or "On_Hold" => HistoryActionType.OnHold,
        "Rewatching" => HistoryActionType.Rewatching,
        "AddedToList" or "Added" => HistoryActionType.AddedToList,
        "Edited" => HistoryActionType.Edited,
        _ => HistoryActionType.Watched
    };

    public static string ToActionString(this HistoryActionType actionType) => actionType switch
    {
        HistoryActionType.Watched => "Watched",
        HistoryActionType.Reverted => "Reverted",
        HistoryActionType.SyncFailed => "SyncFailed",
        HistoryActionType.Scrobbled => "Scrobbled",
        HistoryActionType.ScoreSet => "ScoreSet",
        HistoryActionType.Completed => "Completed",
        HistoryActionType.Dropped => "Dropped",
        HistoryActionType.Deleted => "Deleted",
        HistoryActionType.PlanToWatch => "PlanToWatch",
        HistoryActionType.OnHold => "OnHold",
        HistoryActionType.Rewatching => "Rewatching",
        HistoryActionType.AddedToList => "AddedToList",
        HistoryActionType.Edited => "Edited",
        _ => "Watched"
    };
}
