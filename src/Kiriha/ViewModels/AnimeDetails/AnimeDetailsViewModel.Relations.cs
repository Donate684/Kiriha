using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Domain.Extensions;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Models;
using Serilog;

namespace Kiriha.ViewModels.AnimeDetails;

public partial class AnimeDetailsViewModel
{
    private async Task FetchRelationImageAsync(RelationItemVm vm)
    {
        var type = vm.Relation.TargetType?.ToLowerInvariant() ?? "";
        bool isAnime = type == "anime" || type == "tv" || type == "movie" || type == "ova" || type == "ona" || type == "special";

        var existing = _animeRepo.Collection.FirstOrDefault(x => x.Id == vm.Relation.TargetMalId && (isAnime ? x.MediaKind == MediaKind.Anime : x.MediaKind != MediaKind.Anime));
        if (existing != null && !string.IsNullOrEmpty(existing.MainPictureUrl))
        {
            vm.ImageUrl = existing.MainPictureUrl;
            if (!string.IsNullOrEmpty(existing.Type))
            {
                vm.DisplayTargetType = FormatMediaType(existing.Type);
            }
            return;
        }

        try
        {
            AnimeEntity? details = null;
            if (isAnime)
            {
                details = await _malApiService.GetAnimeDetailsAsync(vm.Relation.TargetMalId);
            }
            else
            {
                details = await _malApiService.GetMangaDetailsAsync(vm.Relation.TargetMalId);
            }

            if (details != null)
            {
                if (!string.IsNullOrEmpty(details.MainPictureUrl))
                {
                    vm.ImageUrl = details.MainPictureUrl;
                }
                if (!string.IsNullOrEmpty(details.Type))
                {
                    vm.DisplayTargetType = FormatMediaType(details.Type);
                }
            }
        }
        catch (System.Exception ex)
        {
            Log.Warning(ex, "Failed to fetch image for relation {TargetMalId}", vm.Relation.TargetMalId);
        }
    }

    private static string FormatMediaType(string type)
    {
        if (string.IsNullOrEmpty(type)) return "Unknown";
        var t = type.ToLowerInvariant();
        return t switch
        {
            "light_novel" => "Light Novel",
            "novel" => "Novel",
            "one_shot" => "One-shot",
            "doujinshi" => "Doujinshi",
            "manhwa" => "Manhwa",
            "manhua" => "Manhua",
            "oel" => "OEL",
            "manga" => "Manga",
            "tv" => "TV",
            "movie" => "Movie",
            "ova" => "OVA",
            "ona" => "ONA",
            "special" => "Special",
            "music" => "Music",
            _ => t.UppercaseFirst()
        };
    }

    [RelayCommand]
    private async Task NavigateToRelation(AnimeRelation relation)
    {
        if (relation is null || string.IsNullOrEmpty(relation.TargetType)) return;

        var type = relation.TargetType.ToLowerInvariant();
        MediaKind kind;

        if (type == "manga" || type == "manhwa" || type == "manhua" || type == "novel" || type == "light novel" || type == "one-shot" || type == "doujinshi" || type == "light_novel")
        {
            kind = type.Contains("novel") ? MediaKind.LightNovel : MediaKind.Manga;
        }
        else if (type == "anime" || type == "tv" || type == "movie" || type == "ova" || type == "ona" || type == "special")
        {
            kind = MediaKind.Anime;
        }
        else
        {
            kind = MediaKind.Anime;
        }

        var targetAnime = new AnimeEntity
        {
            Id = relation.TargetMalId,
            Title = relation.TargetName,
            MediaKind = kind
        };

        // If the item exists in the collection, use the full one to ensure all offline fields are loaded.
        var existing = _animeRepo.Collection.FirstOrDefault(x => x.Id == targetAnime.Id && x.MediaKind == targetAnime.MediaKind);
        await _dialogs.ShowAnimeDetailsAsync(null, existing ?? targetAnime);
    }

    private async Task LoadFranchiseAndRelationsAsync()
    {
        // 1. Try to load rich Shikimori franchise timeline
        try
        {
            var data = await _shikiApiService.GetFranchiseAsync(Anime.Id);
            if (data != null && data.Nodes.Count > 1)
            {
                if (data.CurrentId == 0) data.CurrentId = Anime.Id;

                var options = new Kiriha.Utils.Graphs.FranchiseLayoutOptions
                {
                    HideSpecials = true,
                    AnimeOnly = true
                };

                var layout = Kiriha.Utils.Graphs.FranchiseLayoutEngine.CalculateLayout(data, options);
                if (layout.TimelineNodes.Count > 1)
                {
                    foreach (var node in layout.TimelineNodes)
                    {
                        EnrichFranchiseNode(node);
                        _ = FetchFranchiseNodeImageAsync(node);
                    }

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        FranchiseTimeline.Clear();
                        foreach (var node in layout.TimelineNodes)
                        {
                            FranchiseTimeline.Add(node);
                        }
                        HasFranchiseTimeline = FranchiseTimeline.Count > 0;
                        UpdateFranchiseProgressStats();
                    });
                }
            }
        }
        catch (System.Exception ex)
        {
            Log.Warning(ex, "Failed to load franchise timeline for {Id}", Anime.Id);
        }

        // 2. Also load standard relations (used as fallback or for manga relations)
        try
        {
            var relations = await _jikanApiService.GetRelationsAsync(Anime.Id, Anime.MediaKind);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Relations.Clear();
                foreach (var r in relations)
                {
                    var vm = new RelationItemVm(r);
                    Relations.Add(vm);
                    _ = FetchRelationImageAsync(vm);
                }
            });
        }
        catch (System.Exception ex)
        {
            Log.Warning(ex, "Failed to fetch relations for {Id}", Anime.Id);
        }
    }

    private void EnrichFranchiseNode(Kiriha.Utils.Graphs.FranchiseGraphVisualNode node)
    {
        bool isManga = node.IsMangaOrNovel;
        var existing = _animeRepo.Collection.FirstOrDefault(x =>
            x.Id == node.Node.Id &&
            (isManga ? x.MediaKind != MediaKind.Anime : x.MediaKind == MediaKind.Anime));

        if (existing != null)
        {
            node.UserStatus = existing.Status;
            node.UserProgress = existing.Progress;
            node.TotalEpisodes = existing.TotalEpisodes;
            node.Score = existing.Score;
            node.RussianTitle = existing.RussianTitle;

            if (!string.IsNullOrEmpty(existing.MainPictureUrl))
            {
                node.DisplayImageUrl = existing.MainPictureUrl;
            }
        }
    }

    private async Task FetchFranchiseNodeImageAsync(Kiriha.Utils.Graphs.FranchiseGraphVisualNode node)
    {
        if (!string.IsNullOrEmpty(node.DisplayImageUrl)) return;

        bool isManga = node.IsMangaOrNovel;
        try
        {
            AnimeEntity? details = isManga
                ? await _malApiService.GetMangaDetailsAsync(node.Node.Id)
                : await _malApiService.GetAnimeDetailsAsync(node.Node.Id);

            if (details != null && !string.IsNullOrEmpty(details.MainPictureUrl))
            {
                node.DisplayImageUrl = details.MainPictureUrl;
            }
        }
        catch (System.Exception ex)
        {
            Log.Warning(ex, "Failed to fetch image for franchise node {Id}", node.Node.Id);
        }
    }

    [RelayCommand]
    private async Task NavigateToFranchiseNode(Kiriha.Utils.Graphs.FranchiseGraphVisualNode node)
    {
        if (node is null || node.Node is null) return;
        if (node.IsCurrent) return;

        MediaKind kind = node.Node.Kind.ToLowerInvariant() switch
        {
            "manga" or "manhwa" or "manhua" or "one_shot" or "doujin" => MediaKind.Manga,
            "novel" or "light_novel" => MediaKind.LightNovel,
            _ => MediaKind.Anime
        };

        var existing = _animeRepo.Collection.FirstOrDefault(x => x.Id == node.Node.Id && x.MediaKind == kind);
        if (existing != null)
        {
            await _dialogs.ShowAnimeDetailsAsync(null, existing);
            return;
        }

        var targetAnime = new AnimeEntity
        {
            Id = node.Node.Id,
            Title = node.Node.Name,
            RussianTitle = node.RussianTitle,
            MediaKind = kind,
            MainPictureUrl = !string.IsNullOrEmpty(node.DisplayImageUrl) ? node.DisplayImageUrl : node.Node.ImageUrl,
            Status = node.UserStatus,
            Progress = node.UserProgress ?? 0,
            TotalEpisodes = node.TotalEpisodes ?? 0
        };

        await _dialogs.ShowAnimeDetailsAsync(null, targetAnime);
    }

    public void UpdateFranchiseProgressStats()
    {
        int total = FranchiseTimeline.Count;
        if (total == 0)
        {
            FranchiseCompletedCount = 0;
            FranchiseTotalCount = 0;
            FranchiseCompletionPercentage = 0;
            FranchiseProgressSummary = string.Empty;
            FranchiseCompletionPercentText = "0%";
            FranchiseProgressTooltip = string.Empty;
            return;
        }

        int completed = FranchiseTimeline.Count(n => n.UserStatus == UserAnimeStatus.Completed);

        double totalWeight = 0;
        foreach (var node in FranchiseTimeline)
        {
            if (node.UserStatus == UserAnimeStatus.Completed)
            {
                totalWeight += 1.0;
            }
            else if (node.UserStatus == UserAnimeStatus.Watching)
            {
                if (node.TotalEpisodes.HasValue && node.TotalEpisodes.Value > 0 && node.UserProgress.HasValue)
                {
                    totalWeight += System.Math.Clamp((double)node.UserProgress.Value / node.TotalEpisodes.Value, 0.0, 0.99);
                }
                else
                {
                    totalWeight += 0.5;
                }
            }
        }

        int percent = System.Math.Clamp((int)System.Math.Round((totalWeight / total) * 100), 0, 100);
        if (completed == total) percent = 100;

        FranchiseCompletedCount = completed;
        FranchiseTotalCount = total;
        FranchiseCompletionPercentage = percent;
        FranchiseProgressSummary = $"{completed} / {total}";
        FranchiseCompletionPercentText = $"{percent}%";
        FranchiseProgressTooltip = string.Format(Localization.LocalizationStore.Translate("anime.labels.franchise_progress_tooltip"), completed, total, percent);
    }
}
