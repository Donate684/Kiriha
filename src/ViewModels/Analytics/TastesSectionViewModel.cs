using Kiriha.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core;
using Kiriha.Models.Entities;

namespace Kiriha.ViewModels.Analytics;

public partial class TastesSectionViewModel : ViewModelBase
{
    public ObservableCollection<AnalyticsBar> GenreDistribution { get; } = new();
    public ObservableCollection<AnalyticsBar> StudioDistribution { get; } = new();
    public ObservableCollection<AnalyticsBar> TasteHighlights { get; } = new();
    public ObservableCollection<AnalyticsFavoriteRow> FavoriteGenres { get; } = new();
    public ObservableCollection<AnalyticsFavoriteRow> FavoriteStudios { get; } = new();

    public void Refresh(IReadOnlyCollection<AnimeItem> items, IReadOnlyCollection<AnimeItem> nonPlanned)
    {
        GenreDistribution.Clear();
        StudioDistribution.Clear();
        TasteHighlights.Clear();
        FavoriteGenres.Clear();
        FavoriteStudios.Clear();

        if (items.Count == 0) return;

        AddTopDistribution(GenreDistribution, nonPlanned.SelectMany(x => x.Genres), 8);
        AddTopDistribution(StudioDistribution, nonPlanned.SelectMany(x => x.Studios), 8);
        AddTasteHighlights();
        AddFavoriteRows(FavoriteGenres, nonPlanned, x => x.Genres, LocalizeGenre);
        AddFavoriteRows(FavoriteStudios, nonPlanned, x => x.Studios);
    }

    private static void AddTopDistribution(ObservableCollection<AnalyticsBar> target, IEnumerable<string> values, int take)
    {
        var groups = values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x.Trim())
            .Select(x => new { Label = x.Key, Count = x.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Label)
            .Take(take)
            .ToList();

        var max = groups.Count == 0 ? 1 : groups.Max(x => x.Count);
        foreach (var group in groups)
        {
            var share = group.Count * 100.0 / max;
            target.Add(new AnalyticsBar
            {
                Label = group.Label,
                Value = group.Count.ToString("N0"),
                Count = group.Count,
                Percent = AnalyticsHelpers.Percent(group.Count, max),
                ShareText = $"{share:0}%",
                Accent = AnalyticsHelpers.GetAccent(group.Label)
            });
        }
    }

    private void AddTasteHighlights()
    {
        foreach (var item in GenreDistribution.Take(3))
        {
            TasteHighlights.Add(new AnalyticsBar
            {
                Label = item.Label,
                Value = item.Value,
                Count = item.Count,
                Percent = item.Percent,
                ShareText = item.ShareText,
                Accent = item.Accent
            });
        }
    }

    private static void AddFavoriteRows(
        ObservableCollection<AnalyticsFavoriteRow> target,
        IEnumerable<AnimeItem> items,
        Func<AnimeItem, IEnumerable<string>> selector,
        Func<string, string>? nameFormatter = null)
    {
        var groups = items
            .SelectMany(item => selector(item)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => new { Key = value.Trim(), Item = item }))
            .GroupBy(x => x.Key)
            .Select(group =>
            {
                var entries = group.Select(x => x.Item).DistinctBy(x => x.Id).ToList();
                var scores = entries
                    .Select(x => int.TryParse(x.Score, out var score) ? score : 0)
                    .Where(x => x > 0)
                    .ToList();
                var mean = scores.Count > 0 ? scores.Average() : 0;
                var weighted = scores.Count > 0 ? FavoriteScore(mean, entries.Count) : 0;
                var hours = AnalyticsHelpers.EstimateHoursWatched(entries);

                return new
                {
                    Name = group.Key,
                    Count = entries.Count,
                    Mean = mean,
                    Weighted = weighted,
                    Hours = hours,
                    Entries = entries
                };
            })
            .OrderByDescending(x => x.Weighted)
            .ThenByDescending(x => x.Count)
            .ThenBy(x => x.Name)
            .Take(30)
            .ToList();

        var totalCompleted = items.Count(x => x.Status == UserAnimeStatus.Completed);
        if (totalCompleted <= 0) totalCompleted = 1;

        var rank = 1;
        foreach (var group in groups)
        {
            var name = nameFormatter?.Invoke(group.Name) ?? group.Name;
            var mean = group.Mean > 0 ? group.Mean.ToString("0.00") : "-";
            var weighted = group.Weighted > 0 ? group.Weighted.ToString("0.00") : "-";
            var hours = $"{group.Hours:0.0} ч";

            var completedInGroup = group.Entries.Count(x => x.Status == UserAnimeStatus.Completed);
            var percentCompleted = totalCompleted > 0 ? (completedInGroup * 100.0 / totalCompleted) : 0;

            var row = new AnalyticsFavoriteRow
            {
                Rank = rank++,
                Name = name,
                Count = group.Count,
                MeanScore = mean,
                WeightedScore = weighted,
                TimeSpent = hours,
                Summary = $"{group.Count} тайтл. • оценка {mean} • {hours}",
                Percent = percentCompleted,
                Accent = AnalyticsHelpers.GetAccent(group.Name)
            };

            foreach (var entry in group.Entries
                         .OrderByDescending(x => int.TryParse(x.Score, out var score) ? score : 0)
                         .ThenBy(x => x.Presentation.DisplayTitle)
                         .Select(x => new AnalyticsHistoryEntry
                         {
                             Title = x.Presentation.DisplayTitle,
                             Subtitle = x.RussianTitle != null ? x.Title : null,
                             Detail = int.TryParse(x.Score, out var score) && score > 0
                                 ? $"Оценка {score}"
                                 : (x.TotalEpisodes > 0 ? x.TotalEpisodes.ToString() : "?"),
                             PosterUrl = x.MainPictureUrl
                         }))
            {
                row.Entries.Add(entry);
            }

            target.Add(row);
        }
    }

    private static double FavoriteScore(double meanScore, int count)
    {
        const double globalMean = 5.5;
        const double smoothing = 10.0;
        var bayesianMean = (meanScore * count + globalMean * smoothing) / (count + smoothing);
        var volumeBonus = Math.Log10(Math.Max(1, count));
        return bayesianMean + volumeBonus;
    }

    private static string LocalizeGenre(string genre)
    {
        var candidates = new[]
        {
            genre.ToLowerInvariant().Replace(" ", string.Empty),
            ToResourceKey(genre),
            genre.ToLowerInvariant()
        };

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var key = $"genres.{candidate}";
            var translated = UIUtils.GetLoc(key);
            if (translated != key)
            {
                return translated;
            }
        }

        return genre;
    }

    private static string ToResourceKey(string value)
    {
        var chars = new List<char>(value.Length);
        var lastWasSeparator = false;
        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                chars.Add(c);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                chars.Add('_');
                lastWasSeparator = true;
            }
        }

        return new string(chars.ToArray()).Trim('_');
    }
}

