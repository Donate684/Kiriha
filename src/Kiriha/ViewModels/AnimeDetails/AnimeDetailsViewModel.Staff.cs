using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Media;
using Serilog;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Constants;

namespace Kiriha.ViewModels.AnimeDetails;

public partial class AnimeDetailsViewModel
{
    private async Task ProcessStaffPlusAsync(System.Collections.Generic.List<AnimeStaff> staffList)
    {
        string[] keyRoles = ["Original Creator", "Director", "Series Composition", "Script", "Music", "Character Design"];
        var staffPlusVms = new System.Collections.Generic.List<StaffPlusItemVm>();

        foreach (var s in staffList)
        {
            if (string.IsNullOrEmpty(s.Positions)) continue;
            var roles = s.Positions.Split(',').Select(r => r.Trim()).ToList();
            var matchedRole = roles.FirstOrDefault(r => keyRoles.Contains(r));
            if (matchedRole == null) continue;

            if (staffPlusVms.Count >= 10) break;
            if (s.PersonMalId == 0) continue;

            staffPlusVms.Add(new StaffPlusItemVm(s) { Role = matchedRole });
        }

        var sorted = staffPlusVms.OrderByDescending(x => x.Role == "Original Creator").ThenBy(x => x.Role).ToList();

        Dispatcher.UIThread.Post(() =>
        {
            StaffPlus.Clear();
            foreach (var vm in sorted)
            {
                StaffPlus.Add(vm);
            }
        });

        foreach (var vm in sorted)
        {
            _ = FetchStaffWorksAsync(vm);
        }
    }

    private async Task FetchStaffWorksAsync(StaffPlusItemVm vm)
    {
        try
        {
            var personData = await _shikiApiService.GetPersonWorksAsync(vm.Staff.PersonMalId);
            if (personData?.Works != null)
            {
                var validWorks = personData.Works
                    .Where(w => w.Anime != null && w.Anime.Id != Anime.Id)
                    .Where(w => w.Role != null && IsRoleMatch(w.Role, vm.Role))
                    .Select(w => new { Work = w, Score = double.TryParse(w.Anime!.Score, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var score) ? score : 0 })
                    .OrderByDescending(x => x.Score)
                    .ToList();

                if (validWorks.Count > 0)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var w in validWorks)
                        {
                            string scoreDisplay = w.Score > 0 ? w.Work.Anime!.Score! : "-";

                            var localAnime = _animeRepo.Collection.FirstOrDefault(a => a.Id == w.Work.Anime!.Id);
                            IBrush highlight = Brushes.Transparent;
                            if (localAnime != null)
                            {
                                if (localAnime.Status == UserAnimeStatus.Watching || localAnime.Status == UserAnimeStatus.Completed)
                                    highlight = SolidColorBrush.Parse("#334CAF50");
                                else if (localAnime.Status == UserAnimeStatus.Dropped)
                                    highlight = SolidColorBrush.Parse("#33F44336");
                            }

                            vm.BestWorks.Add(new StaffWorkVm
                            {
                                Title = string.IsNullOrEmpty(w.Work.Anime!.Russian) ? (w.Work.Anime!.Name ?? "Unknown") : w.Work.Anime.Russian,
                                Url = Kiriha.Core.Domain.Constants.AppConstants.Api.Shiki.OneHost + w.Work.Anime.Url,
                                Score = scoreDisplay,
                                HighlightColor = highlight
                            });
                        }
                    });
                }
                else
                {
                    Dispatcher.UIThread.Post(() => StaffPlus.Remove(vm));
                }
            }
            else
            {
                Dispatcher.UIThread.Post(() => StaffPlus.Remove(vm));
            }
        }
        catch (System.Exception ex)
        {
            Log.Warning(ex, "Failed to fetch person works for {PersonId}", vm.Staff.PersonMalId);
            Dispatcher.UIThread.Post(() => StaffPlus.Remove(vm));
        }
    }

    private bool IsRoleMatch(string role, string matchedRole)
    {
        var r = role.ToLowerInvariant();
        return matchedRole switch
        {
            "Original Creator" => AppConstants.StaffRoles.OriginalCreator.Any(r.Contains),
            "Director" => AppConstants.StaffRoles.Director.Any(r.Contains) &&
                          !AppConstants.StaffRoles.NotDirector.Any(r.Contains) &&
                          !r.Contains("cg") && !r.Contains("3d"),
            "Series Composition" => AppConstants.StaffRoles.SeriesComposition.Any(r.Contains),
            "Script" => AppConstants.StaffRoles.Script.Any(r.Contains),
            "Music" => AppConstants.StaffRoles.Music.Any(r.Contains),
            "Character Design" => AppConstants.StaffRoles.CharacterDesign.Any(r.Contains),
            _ => r.Contains(matchedRole.ToLowerInvariant())
        };
    }
}
