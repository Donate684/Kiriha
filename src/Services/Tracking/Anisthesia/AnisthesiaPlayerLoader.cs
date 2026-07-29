using System;
using System.Collections.Generic;
using System.IO;
using Kiriha.Models;
using Serilog;

namespace Kiriha.Services.Tracking.Anisthesia;

public static class AnisthesiaPlayerLoader
{
    public static IReadOnlyList<AnisthesiaPlayer> Load()
    {
        try
        {
            var uri = new Uri("avares://Kiriha/Assets/Anisthesia/players.anisthesia");
            using var stream = Avalonia.Platform.AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            var data = reader.ReadToEnd();
            var players = PlayerParser.ParseData(data);
            Log.Information("Loaded {Count} players from Anisthesia embedded data.", players.Count);
            return players;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AnisthesiaPlayerLoader: Failed to load embedded players data. Fallback to empty list.");
            return new List<AnisthesiaPlayer>();
        }
    }
}
