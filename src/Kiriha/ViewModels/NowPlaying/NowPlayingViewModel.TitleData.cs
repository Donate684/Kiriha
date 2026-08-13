using Kiriha.Core.Domain.Models.Entities;
using System;
using System.Linq;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.ViewModels.Settings;

namespace Kiriha.ViewModels.NowPlaying;

public partial class NowPlayingViewModel
{
    /// <summary>
    /// Resolved user-defined share buttons for the currently matched anime.
    /// Refreshed via <see cref="OnMatchedAnimeChanged"/>.
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<CustomShareLinkRuntime> CustomShareLinks { get; } = new();

    private System.Collections.Generic.IReadOnlyList<string> _allAlternativeTitles = Array.Empty<string>();
    public System.Collections.Generic.IEnumerable<string> AllAlternativeTitles => _allAlternativeTitles;

    public bool HasAlternativeTitles => AllAlternativeTitles.Any();

    partial void OnMatchedAnimeChanged(AnimeEntity? value)
    {
        CustomShareLinks.Clear();
        
        if (value == null)
        {
            _allAlternativeTitles = Array.Empty<string>();
            return;
        }

        var list = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(value.EnglishTitle) && value.EnglishTitle != value.Title)
            list.Add(value.EnglishTitle);
        if (!string.IsNullOrEmpty(value.JapaneseTitle) && value.JapaneseTitle != value.Title)
            list.Add(value.JapaneseTitle);

        foreach (var syn in value.AlternativeTitles)
        {
            if (syn != value.Title && !list.Contains(syn))
                list.Add(syn);
        }
        _allAlternativeTitles = list;

        foreach (var link in _settingsService.Current.CustomLinks)
        {
            if (string.IsNullOrWhiteSpace(link.UrlTemplate)) continue;
            var url = Kiriha.Core.CustomLinkResolver.Resolve(link.UrlTemplate, value);
            CustomShareLinks.Add(new CustomShareLinkRuntime(link.Name, link.IconKind, url, link.IconPath));
        }
    }
}
