using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Services.Data.Core;

public static class LegacyUserStateMigration
{
    public static async Task MigrateAsync(AppDbContext context, string? legacyConfigPath = null, CancellationToken ct = default)
    {
        var configPath = legacyConfigPath ?? PathHelper.GetLegacySettingsPath();
        if (!File.Exists(configPath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(configPath, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool hasChanges = false;

            if (root.TryGetProperty("UI", out var ui) && ui.TryGetProperty("HiddenSeasonalIds", out var seasonalProp) && seasonalProp.ValueKind == JsonValueKind.Array)
            {
                var existingSeasonal = await context.HiddenSeasonalAnime.Select(x => x.AnimeId).ToHashSetAsync(ct);
                foreach (var item in seasonalProp.EnumerateArray())
                {
                    if (item.TryGetInt32(out var id) && !existingSeasonal.Contains(id))
                    {
                        context.HiddenSeasonalAnime.Add(new HiddenSeasonalAnime
                        {
                            AnimeId = id,
                            CreatedAt = DateTime.UtcNow
                        });
                        existingSeasonal.Add(id);
                        hasChanges = true;
                    }
                }
            }

            if (root.TryGetProperty("Torrents", out var torrents))
            {
                if (torrents.TryGetProperty("HiddenAnimeIds", out var hiddenProp) && hiddenProp.ValueKind == JsonValueKind.Array)
                {
                    var existingHidden = await context.HiddenTorrentAnime.Select(x => x.AnimeId).ToHashSetAsync(ct);
                    foreach (var item in hiddenProp.EnumerateArray())
                    {
                        if (item.TryGetInt32(out var id) && !existingHidden.Contains(id))
                        {
                            context.HiddenTorrentAnime.Add(new HiddenTorrentAnime
                            {
                                AnimeId = id,
                                CreatedAt = DateTime.UtcNow
                            });
                            existingHidden.Add(id);
                            hasChanges = true;
                        }
                    }
                }

                if (torrents.TryGetProperty("PerTitleFilters", out var filtersProp) && filtersProp.ValueKind == JsonValueKind.Object)
                {
                    var existingFilters = await context.TorrentTitleFilters.Select(x => x.AnimeId).ToHashSetAsync(ct);
                    foreach (var prop in filtersProp.EnumerateObject())
                    {
                        if (int.TryParse(prop.Name, out var id) && !existingFilters.Contains(id))
                        {
                            var f = prop.Value;
                            var entity = new TorrentTitleFilter
                            {
                                AnimeId = id,
                                OnlyCrunchyroll = f.TryGetProperty("OnlyCrunchyroll", out var oc) && oc.GetBoolean(),
                                FilterNetflix = f.TryGetProperty("FilterNetflix", out var fn) && fn.GetBoolean(),
                                FilterAmazon = f.TryGetProperty("FilterAmazon", out var fa) && fa.GetBoolean(),
                                FilterHidive = f.TryGetProperty("FilterHidive", out var fh) && fh.GetBoolean(),
                                FilterVaryg = f.TryGetProperty("FilterVaryg", out var fv) && fv.GetBoolean(),
                                FilterEraiRaws = f.TryGetProperty("FilterEraiRaws", out var fe) && fe.GetBoolean(),
                                FilterToonsHub = f.TryGetProperty("FilterToonsHub", out var ft) && ft.GetBoolean(),
                                FilterJudas = f.TryGetProperty("FilterJudas", out var fj) && fj.GetBoolean(),
                                FilterHevc = f.TryGetProperty("FilterHevc", out var fhe) && fhe.GetBoolean(),
                                Filter1080p = f.TryGetProperty("Filter1080p", out var fp) && fp.GetBoolean(),
                                UseCustomQuery = f.TryGetProperty("UseCustomQuery", out var uc) && uc.GetBoolean(),
                                CustomQuery = f.TryGetProperty("CustomQuery", out var cq) ? cq.GetString() : null,
                                UpdatedAt = DateTime.UtcNow
                            };
                            context.TorrentTitleFilters.Add(entity);
                            existingFilters.Add(id);
                            hasChanges = true;
                        }
                    }
                }
            }

            if (hasChanges)
            {
                await context.SaveChangesAsync(ct);
                Log.Information("Legacy user state migrated successfully to SQLite from {Path}", configPath);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to migrate legacy user state from {Path}", configPath);
        }
    }
}
