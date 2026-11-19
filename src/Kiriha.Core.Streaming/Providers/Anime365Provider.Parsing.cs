using System.Text.Json;
using System.Text.RegularExpressions;
using Kiriha.Core.Streaming.Constants;

namespace Kiriha.Core.Streaming.Providers;

public sealed partial class Anime365Provider
{
    private static void AddDefaultHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);
        request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Accept, "application/json");
        request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.AcceptLanguage, StreamingConstants.Headers.RuRuLanguage);
    }

    private static string ExtractTitle(JsonElement element)
    {
        if (element.TryGetProperty("titles", out var titlesProp) && titlesProp.ValueKind == JsonValueKind.Object)
        {
            if (titlesProp.TryGetProperty("ru", out var ruProp) && ruProp.ValueKind == JsonValueKind.String && ruProp.GetString() is { } ru && !string.IsNullOrWhiteSpace(ru))
                return ru.Trim();
            if (titlesProp.TryGetProperty("romaji", out var roProp) && roProp.ValueKind == JsonValueKind.String && roProp.GetString() is { } ro && !string.IsNullOrWhiteSpace(ro))
                return ro.Trim();
            if (titlesProp.TryGetProperty("en", out var enProp) && enProp.ValueKind == JsonValueKind.String && enProp.GetString() is { } en && !string.IsNullOrWhiteSpace(en))
                return en.Trim();
        }

        if (element.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String && titleProp.GetString() is { } t && !string.IsNullOrWhiteSpace(t))
            return t.Trim();

        return SafeGetId(element) ?? "Anime365";
    }

    private static string? ExtractPoster(JsonElement element)
    {
        if (element.TryGetProperty("posterUrlSmall", out var smallProp) && smallProp.ValueKind == JsonValueKind.String && smallProp.GetString() is { } s && !string.IsNullOrWhiteSpace(s))
            return s;

        if (element.TryGetProperty("posterUrl", out var urlProp) && urlProp.ValueKind == JsonValueKind.String && urlProp.GetString() is { } u && !string.IsNullOrWhiteSpace(u))
            return u;

        return null;
    }

    private static string? ExtractDescription(JsonElement dataProp)
    {
        if (!dataProp.TryGetProperty("descriptions", out var descProp))
            return null;

        if (descProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in descProp.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("value", out var valProp) &&
                    valProp.ValueKind == JsonValueKind.String &&
                    valProp.GetString() is { } v &&
                    !string.IsNullOrWhiteSpace(v))
                {
                    return v.Trim();
                }
            }
        }
        else if (descProp.ValueKind == JsonValueKind.Object)
        {
            if (descProp.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String && textProp.GetString() is { } t && !string.IsNullOrWhiteSpace(t))
                return t.Trim();
            if (descProp.TryGetProperty("html", out var htmlProp) && htmlProp.ValueKind == JsonValueKind.String && htmlProp.GetString() is { } h && !string.IsNullOrWhiteSpace(h))
                return h.Trim();
        }

        return null;
    }

    private static bool SafeIsActive(JsonElement element)
    {
        if (element.TryGetProperty("isActive", out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var num) && num == 0)
                return false;
            if (prop.ValueKind == JsonValueKind.False)
                return false;
            if (prop.ValueKind == JsonValueKind.String && prop.GetString() is "0" or "false")
                return false;
        }
        return true;
    }

    private static string? SafeGetId(JsonElement element)
    {
        if (element.TryGetProperty("id", out var idProp))
        {
            if (idProp.ValueKind == JsonValueKind.Number)
                return idProp.GetInt64().ToString();
            if (idProp.ValueKind == JsonValueKind.String && idProp.GetString() is { } s && !string.IsNullOrWhiteSpace(s))
                return s;
        }
        return null;
    }

    private static int SafeGetInt(JsonElement element, string propertyName, int defaultValue = 0)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
            {
                if (prop.TryGetInt32(out var i))
                    return i;
                if (prop.TryGetDouble(out var d))
                    return (int)Math.Round(d);
            }
            else if (prop.ValueKind == JsonValueKind.String && prop.GetString() is { } str)
            {
                if (int.TryParse(str, out var parsed))
                    return parsed;
                if (double.TryParse(str, System.Globalization.CultureInfo.InvariantCulture, out var d))
                    return (int)Math.Round(d);
            }
        }
        return defaultValue;
    }

    private static string? SafeGetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }

    private static string ExtractSeriesId(string animeUrlOrId)
    {
        var match = SeriesIdRegex().Match(animeUrlOrId);
        return match.Success ? match.Groups[1].Value : animeUrlOrId.Trim();
    }

    [GeneratedRegex(@"(\d+)")]
    private static partial Regex SeriesIdRegex();
}
