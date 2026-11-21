using System.Text.Json;
using Serilog;

namespace Kiriha.Core.Tracking.Api;

internal static class CatalogSearchCachePolicy
{
    private static readonly TimeSpan ResultsFreshness = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan EmptyFreshness = TimeSpan.FromMinutes(2);

    public static TimeSpan? Mal(byte[] body) => ReadFreshness(body, MalResultCount);

    public static TimeSpan? Shiki(byte[] body) => ReadFreshness(body, ShikiResultCount);

    private static TimeSpan? ReadFreshness(byte[] body, Func<JsonElement, int?> countResults)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return countResults(json.RootElement) switch
            {
                null => null,
                0 => EmptyFreshness,
                _ => ResultsFreshness
            };
        }
        catch (JsonException ex)
        {
            Log.Debug(ex, "Invalid catalog search response");
            return null;
        }
    }

    private static int? MalResultCount(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var entry in data.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("node", out var node) ||
                node.ValueKind != JsonValueKind.Object ||
                !node.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number ||
                !id.TryGetInt32(out var value) || value <= 0 || !HasText(node, "title"))
                return null;
        }
        return data.GetArrayLength();
    }

    private static int? ShikiResultCount(JsonElement root) => root.ValueKind == JsonValueKind.Array &&
        root.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Object && HasText(item, "name"))
            ? root.GetArrayLength()
            : null;

    private static bool HasText(JsonElement item, string property) =>
        item.TryGetProperty(property, out var text) && text.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(text.GetString());
}
