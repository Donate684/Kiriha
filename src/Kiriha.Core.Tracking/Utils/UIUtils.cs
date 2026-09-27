using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core;

internal static class UIUtils
{
    public static string GetLoc(string key, params object?[] args)
    {
        return AnimeEntityPresentation.DefaultLocalizer.GetLoc(key, args);
    }
}
