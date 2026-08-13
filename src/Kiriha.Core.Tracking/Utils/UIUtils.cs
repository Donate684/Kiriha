namespace Kiriha.Core;

// Mock implementation for the domain layer until a proper ILocalizationService is introduced.
internal static class UIUtils
{
    public static string GetLoc(string key, params object[] args)
    {
        if (args == null || args.Length == 0) return key;
        return $"{key} [{string.Join(", ", args)}]";
    }
}
