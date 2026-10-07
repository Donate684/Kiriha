using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Tracking.Utils;

/// <summary>
/// Helper for formatting localized strings within tracking operations
/// using the shared domain localizer.
/// </summary>
internal static class TrackingLoc
{
    public static string GetLoc(string key, params object?[] args)
    {
        return AnimeEntityPresentation.DefaultLocalizer.GetLoc(key, args);
    }
}
