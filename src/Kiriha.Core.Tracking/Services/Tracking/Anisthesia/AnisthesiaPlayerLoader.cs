using System;
using System.Collections.Generic;
using System.IO;

using Kiriha.Core.Domain.Models;
using Serilog;

namespace Kiriha.Core.Tracking.Anisthesia;

public static class AnisthesiaPlayerLoader
{
    public static IReadOnlyList<AnisthesiaPlayer> Load()
    {
        try
        {
            var assembly = typeof(AnisthesiaPlayerLoader).Assembly;
            using var stream = assembly.GetManifestResourceStream("Kiriha.Core.Tracking.Assets.Anisthesia.players.anisthesia");
            if (stream == null)
            {
                Log.Warning("AnisthesiaPlayerLoader: Could not find embedded resource players.anisthesia.");
                return new List<AnisthesiaPlayer>();
            }
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
