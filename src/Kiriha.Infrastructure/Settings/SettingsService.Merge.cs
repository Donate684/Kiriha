using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Serilog;

namespace Kiriha.Infrastructure.Settings;

public partial class SettingsService
{
    // Compare the unencrypted values, but write only the encoded values. DPAPI produces
    // different ciphertext when the same token is encrypted again.
    private static bool ApplyLocalChanges(JsonObject baseline, JsonObject current, JsonObject encoded, JsonObject latest)
    {
        bool changed = false;
        foreach (var (key, value) in current)
        {
            if (baseline.TryGetPropertyValue(key, out var oldValue) && JsonNode.DeepEquals(oldValue, value))
                continue;

            // Collections and legacy OAuth token objects represent complete values.
            // Merging separate token fields could pair credentials from different logins.
            if (key is not ("Mal" or "Shiki") && oldValue is JsonObject oldObject && value is JsonObject newObject &&
                encoded[key] is JsonObject encodedObject && latest[key] is JsonObject latestObject)
            {
                changed |= ApplyLocalChanges(oldObject, newObject, encodedObject, latestObject);
            }
            else if (!latest.TryGetPropertyValue(key, out var existing) || !JsonNode.DeepEquals(existing, encoded[key]))
            {
                latest[key] = encoded[key]?.DeepClone();
                changed = true;
            }
        }

        foreach (var (key, _) in baseline)
        {
            if (!current.ContainsKey(key))
                changed |= latest.Remove(key);
        }

        return changed;
    }

    private static JsonObject? ReadDocument<T>(string path, JsonTypeInfo<T> typeInfo, out bool hasValidPrimary) where T : class
    {
        var primary = ReadDocument(path, typeInfo);
        hasValidPrimary = primary != null;
        return primary ?? ReadDocument(GetBackupPath(path), typeInfo);
    }

    private static JsonObject? ReadDocument<T>(string path, JsonTypeInfo<T> typeInfo) where T : class
    {
        try
        {
            var json = ReadAllTextShared(path);
            return JsonNode.Parse(json) is JsonObject parsed && JsonSerializer.Deserialize(json, typeInfo) != null ? parsed : null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Ignoring invalid settings document {Path}", path);
            return null;
        }
        // A file locked or denied by another process must fail the save, not be treated as empty.
    }
}
