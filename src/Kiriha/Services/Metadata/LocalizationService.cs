using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Extensions;
using Serilog;

namespace Kiriha.Services.Metadata;

public class LocalizationService : ILocalizer
{
    private static readonly string[] CommonPrefixes =
    [
        "anime.types.", "anime.seasons.", "anime.status.", "common.actions.",
        "common.status.", "filters.sort.", "genres.", "torrents.sort."
    ];

    private static volatile IReadOnlyDictionary<string, string> _activeSnapshot =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static volatile ILocalizer _active = new SnapshotFallbackLocalizer();

    /// <summary>
    /// Active application localizer used by parameterless XAML value converters and static presentation helpers.
    /// Prefer constructor-injected <see cref="ILocalizer"/> in services and ViewModels.
    /// </summary>
    public static ILocalizer Active => _active;

    internal static IReadOnlyDictionary<string, string> ActiveResources => _activeSnapshot;

    private readonly ConcurrentDictionary<string, CompositeFormat> _formatCache = new();
    private volatile IReadOnlyDictionary<string, string> _currentResources =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private string _currentLanguage = AppConstants.Languages.En;

    private readonly string[] _namespaces =
    [
        "common", "navigation", "anime", "genres", "settings",
        "scrobbler", "filters", "history", "wizard", "sync",
        "updates", "auth", "search", "torrents", "notifications", "crash",
        "about", "player", "analytics", "schedule"
    ];

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarAnalyzer.CSharp", "S3010:Static field written in instance method", Justification = "Singleton instance referenced by static localization helpers")]
    public LocalizationService()
    {
        _active = this;
    }

    public string CurrentLanguage => _currentLanguage;
    public CultureInfo CurrentCulture => GetCultureForLanguage(_currentLanguage);

    public static CultureInfo GetCultureForLanguage(string? langCode)
    {
        return string.Equals(langCode, AppConstants.Languages.Ru, StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.GetCultureInfo("ru-RU")
            : CultureInfo.GetCultureInfo("en-US");
    }

    public string GetLoc(string key)
    {
        if (_currentResources.TryGetValue(key, out var cached))
            return cached;

        if (_activeSnapshot.TryGetValue(key, out var snapVal))
            return snapVal;

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess() && Application.Current != null &&
            Application.Current.Resources.TryGetValue($"l.{key}", out var val) && val is string str)
        {
            return str;
        }

        return key;
    }

    public string GetLoc(string key, params object?[] args)
    {
        var pattern = GetLoc(key);
        if (args is null || args.Length == 0) return pattern;
        try
        {
            var composite = _formatCache.GetOrAdd(pattern, static p => CompositeFormat.Parse(p));
            return string.Format(CurrentCulture, composite, args);
        }
        catch
        {
            return pattern;
        }
    }

    /// <summary>
    /// Resolves enum names, <c>l.</c>-prefixed keys, and camelCase/PascalCase tokens
    /// against the thread-safe active resource snapshot (with Avalonia resource fallback on UI thread).
    /// </summary>
    public static string Translate(string keyToUse)
    {
        if (keyToUse.StartsWith("l.", StringComparison.OrdinalIgnoreCase))
            keyToUse = keyToUse[2..];

        var snapshot = _activeSnapshot;
        if (snapshot.Count > 0)
        {
            if (snapshot.TryGetValue(keyToUse, out var directSnap))
                return directSnap;

            string lowerSnap = keyToUse.ToLowerInvariant().Replace(" ", "");
            if (snapshot.TryGetValue(lowerSnap, out var lowerVal))
                return lowerVal;

            string snakeSnap = PascalToSnake(keyToUse);
            if (snakeSnap != lowerSnap && snapshot.TryGetValue(snakeSnap, out var snakeVal))
                return snakeVal;

            foreach (var prefix in CommonPrefixes)
            {
                if (snapshot.TryGetValue($"{prefix}{lowerSnap}", out var nestedLower))
                    return nestedLower;
                if (snakeSnap != lowerSnap && snapshot.TryGetValue($"{prefix}{snakeSnap}", out var nestedSnake))
                    return nestedSnake;
            }
        }

        if (Application.Current is null || !Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            return keyToUse;

        string lowerKey = keyToUse.ToLowerInvariant().Replace(" ", "");
        string snakeKey = PascalToSnake(keyToUse);

        if (Application.Current.Resources.TryGetValue($"l.{lowerKey}", out var translatedLower))
            return translatedLower?.ToString() ?? keyToUse;

        if (snakeKey != lowerKey && Application.Current.Resources.TryGetValue($"l.{snakeKey}", out var translatedSnake))
            return translatedSnake?.ToString() ?? keyToUse;

        foreach (var prefix in CommonPrefixes)
        {
            if (Application.Current.Resources.TryGetValue($"l.{prefix}{lowerKey}", out var translatedNested))
                return translatedNested?.ToString() ?? keyToUse;
            if (snakeKey != lowerKey && Application.Current.Resources.TryGetValue($"l.{prefix}{snakeKey}", out var translatedNestedSnake))
                return translatedNestedSnake?.ToString() ?? keyToUse;
        }

        var parts = keyToUse.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            string firstPartKey = parts[0].ToLowerInvariant();
            if (Application.Current.Resources.TryGetValue($"l.{firstPartKey}", out var firstTranslated))
                return $"{firstTranslated} {parts[1]}";
            if (Application.Current.Resources.TryGetValue($"l.anime.seasons.{firstPartKey}", out var firstTranslatedSeason))
                return $"{firstTranslatedSeason} {parts[1]}";
        }

        string key = keyToUse.Replace(" ", "");
        if (Application.Current.Resources.TryGetValue($"l.{key}", out var translatedExact))
            return translatedExact?.ToString() ?? keyToUse;

        return keyToUse.UppercaseFirst();
    }

    public static string PascalToSnake(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var sb = new StringBuilder(input.Length + 4);
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsWhiteSpace(c) || c == '_' || c == '-')
            {
                if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
                continue;
            }
            if (char.IsUpper(c) && i > 0 && sb.Length > 0 && sb[^1] != '_')
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

#pragma warning disable S2696 // Singleton instance and active snapshot updated on language change
    public void LoadLanguage(string langCode)
    {
        try
        {
            _currentLanguage = langCode;
            _formatCache.Clear();

            var culture = GetCultureForLanguage(langCode);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            LoadAllNamespaces(AppConstants.Languages.En, resources);
            if (!string.Equals(langCode, AppConstants.Languages.En, StringComparison.OrdinalIgnoreCase))
            {
                LoadAllNamespaces(langCode, resources);
            }

            _currentResources = resources;
            _activeSnapshot = resources;
            _active = this;

            if (Application.Current != null)
            {
                void ApplyToAvaloniaResources()
                {
                    var appResources = Application.Current?.Resources;
                    if (appResources is null) return;

                    foreach (var kvp in resources)
                    {
                        var fullKey = $"l.{kvp.Key}";
                        if (!appResources.TryGetValue(fullKey, out var existing) || !string.Equals(existing as string, kvp.Value, StringComparison.Ordinal))
                        {
                            appResources[fullKey] = kvp.Value;
                        }
                    }
                }

                if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                    ApplyToAvaloniaResources();
                else
                    Avalonia.Threading.Dispatcher.UIThread.Post(ApplyToAvaloniaResources);
            }
            Log.Information("Language loaded: {Lang} ({Count} keys, Culture: {Culture})", langCode, resources.Count, culture.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load language: {Lang}", langCode);
        }
    }
#pragma warning restore S2696

    private void LoadAllNamespaces(string langCode, Dictionary<string, string> target)
    {
        foreach (var ns in _namespaces)
        {
            var nsStrings = LoadNamespace(langCode, ns);
            foreach (var kvp in nsStrings)
            {
                target[kvp.Key] = kvp.Value;
            }
        }
    }

    private static Dictionary<string, string> LoadNamespace(string langCode, string ns)
    {
        var result = new Dictionary<string, string>();
        try
        {
            var uri = new Uri($"avares://Kiriha/Assets/i18n/{langCode}/{ns}.json");
            using var stream = Avalonia.Platform.AssetLoader.Open(uri);
            using var doc = JsonDocument.Parse(stream);
            FlattenJson(doc.RootElement, ns, result);
        }
        catch (FileNotFoundException ex)
        {
            Log.Debug(ex, "Namespace {Namespace} not found for language {Lang}", ns, langCode);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load namespace {Namespace} for {Lang}", ns, langCode);
        }
        return result;
    }

    private static void FlattenJson(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    FlattenJson(property.Value, string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}", result);
                }
                break;
            case JsonValueKind.Array:
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    FlattenJson(item, $"{prefix}[{index}]", result);
                    index++;
                }
                break;
            default:
                result[prefix] = element.ToString();
                break;
        }
    }

    private sealed class SnapshotFallbackLocalizer : ILocalizer
    {
        public string GetLoc(string key)
        {
            if (_activeSnapshot.TryGetValue(key, out var snapVal))
                return snapVal;

            if (Application.Current != null && Avalonia.Threading.Dispatcher.UIThread.CheckAccess() &&
                Application.Current.Resources.TryGetValue($"l.{key}", out var val) && val is string str)
            {
                return str;
            }

            return key;
        }

        public string GetLoc(string key, params object?[] args)
        {
            var pattern = GetLoc(key);
            if (args is null || args.Length == 0) return pattern;
            try
            {
                return string.Format(CultureInfo.CurrentCulture, CompositeFormat.Parse(pattern), args);
            }
            catch
            {
                return pattern;
            }
        }
    }
}
