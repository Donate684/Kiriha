using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kiriha.Core.Domain.Models.Api;

public class ShikiFranchiseResponse
{
    [JsonPropertyName("links")]
    public List<ShikiFranchiseLink> Links { get; set; } = new();

    [JsonPropertyName("nodes")]
    public List<ShikiFranchiseNode> Nodes { get; set; } = new();

    [JsonPropertyName("current_id")]
    public int CurrentId { get; set; }
}

public class ShikiFranchiseLink
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("source_id")]
    public int SourceId { get; set; }

    [JsonPropertyName("target_id")]
    public int TargetId { get; set; }

    [JsonPropertyName("source")]
    public int Source { get; set; }

    [JsonPropertyName("target")]
    public int Target { get; set; }

    [JsonPropertyName("weight")]
    public int Weight { get; set; }

    [JsonPropertyName("relation")]
    public string Relation { get; set; } = string.Empty;
}

public class ShikiFranchiseNode
{
    private int? _year;

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("date")]
    [JsonConverter(typeof(ShikiFlexibleDateConverter))]
    public long Date { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("image_url")]
    public string ImageUrl { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public int? Year
    {
        get => _year ?? (Date > 0 ? DateTimeOffset.FromUnixTimeSeconds(Date).Year : null);
        set => _year = value;
    }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("weight")]
    public int Weight { get; set; }
}

/// <summary>
/// Handles both Unix timestamps (official Shikimori: e.g. 1788555600)
/// and Date objects (Shikimori fork: e.g. {"day":5,"month":9,"year":2026}).
/// </summary>
public class ShikiFlexibleDateConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetInt64();

            case JsonTokenType.String:
                var str = reader.GetString();
                if (string.IsNullOrWhiteSpace(str)) return 0;
                if (long.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var num))
                    return num;
                if (System.DateTimeOffset.TryParse(str, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var dto))
                    return dto.ToUnixTimeSeconds();
                return 0;

            case JsonTokenType.StartObject:
                int? year = null;
                int? month = null;
                int? day = null;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject) break;
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        var propName = reader.GetString();
                        reader.Read();
                        if (string.Equals(propName, "year", System.StringComparison.OrdinalIgnoreCase))
                        {
                            if (reader.TokenType == JsonTokenType.Number) year = reader.GetInt32();
                            else if (reader.TokenType == JsonTokenType.String && int.TryParse(reader.GetString(), out var y)) year = y;
                        }
                        else if (string.Equals(propName, "month", System.StringComparison.OrdinalIgnoreCase))
                        {
                            if (reader.TokenType == JsonTokenType.Number) month = reader.GetInt32();
                            else if (reader.TokenType == JsonTokenType.String && int.TryParse(reader.GetString(), out var m)) month = m;
                        }
                        else if (string.Equals(propName, "day", System.StringComparison.OrdinalIgnoreCase))
                        {
                            if (reader.TokenType == JsonTokenType.Number) day = reader.GetInt32();
                            else if (reader.TokenType == JsonTokenType.String && int.TryParse(reader.GetString(), out var d)) day = d;
                        }
                        else
                        {
                            reader.Skip();
                        }
                    }
                }

                if (year.HasValue && year.Value > 0)
                {
                    try
                    {
                        var m = System.Math.Clamp(month ?? 1, 1, 12);
                        var d = System.Math.Clamp(day ?? 1, 1, System.DateTime.DaysInMonth(year.Value, m));
                        var dt = new System.DateTime(year.Value, m, d, 0, 0, 0, System.DateTimeKind.Utc);
                        return new System.DateTimeOffset(dt).ToUnixTimeSeconds();
                    }
                    catch
                    {
                        return 0;
                    }
                }
                return 0;

            case JsonTokenType.Null:
            default:
                return 0;
        }
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}
