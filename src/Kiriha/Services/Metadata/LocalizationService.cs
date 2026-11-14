using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia;
using Kiriha.Core.Abstractions.Services;
using Serilog;
namespace Kiriha.Services.Data.Metadata;
public class LocalizationService : ILocalizer
{
    private static volatile IReadOnlyDictionary<string, string> _activeSnapshot =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyDictionary<string, string> ActiveResources => _activeSnapshot;

    private readonly ConcurrentDictionary<string, CompositeFormat> _formatCache = new();
    private volatile IReadOnlyDictionary<string, string> _currentResources =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private string _currentLanguage = "en";
    private readonly string[] _namespaces =
    {
        "common", "navigation", "anime", "genres", "settings",
        "scrobbler", "filters", "history", "wizard", "sync",
        "updates", "auth", "search", "torrents", "notifications", "crash",
        "about", "player", "analytics", "schedule"
    };
    public string CurrentLanguage => _currentLanguage;
    public CultureInfo CurrentCulture => GetCultureForLanguage(_currentLanguage);

    public static CultureInfo GetCultureForLanguage(string? langCode)
    {
        return string.Equals(langCode, Kiriha.Core.Domain.Constants.AppConstants.Languages.Ru, StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.GetCultureInfo("ru-RU")
            : CultureInfo.GetCultureInfo("en-US");
    }

    public string GetLoc(string key)
    {
        if (_currentResources.TryGetValue(key, out var cached))
            return cached;

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
        catch { return pattern; }
    }
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
            // 1. Load English as base (fallback)
            LoadAllNamespaces("en", resources);
            // 2. If not English, load and override
            if (langCode != "en")
            {
                LoadAllNamespaces(langCode, resources);
            }

            _currentResources = resources;
            _activeSnapshot = resources;

            // 3. Inject into Avalonia resources on UI thread, updating only changed entries
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
    private Dictionary<string, string> LoadNamespace(string langCode, string ns)
    {
        var result = new Dictionary<string, string>();
        try
        {
            var uri = new Uri($"avares://Kiriha/Assets/i18n/{langCode}/{ns}.json");
            using var stream = Avalonia.Platform.AssetLoader.Open(uri);
            using var doc = JsonDocument.Parse(stream);
            FlattenJson(doc.RootElement, ns, result);
        }
        catch (FileNotFoundException)
        {
            Log.Debug("Namespace {Namespace} not found for language {Lang}", ns, langCode);
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to load namespace {Namespace} for {Lang}: {Error}", ns, langCode, ex.Message);
        }
        return result;
    }
    private void FlattenJson(JsonElement element, string prefix, Dictionary<string, string> result)
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
}
