using System;
using System.Text.Json;

namespace Kiriha.Infrastructure.Player;

public static class PipeArgumentSerializer
{
    public static string Serialize(string[] args) => JsonSerializer.Serialize(args);

    public static string[] Deserialize(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(line) ?? [];
        }
        catch (JsonException)
        {
            return line.Split("||", StringSplitOptions.None);
        }
    }
}
