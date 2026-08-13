using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Models;
using Kiriha.Core.Abstractions.Models;
using Kiriha.Core.Abstractions.Models.Entities;
using Kiriha.Core.Tracking.Api;

namespace Kiriha.ViewModels.AnimeDetails;

public partial class AnimeMetadataViewModel : ObservableObject
{
    private readonly AnimeEntity _anime;
    private readonly Kiriha.Core.Services.IMalApiService _malApiService;
    private readonly Kiriha.Core.Services.IShikiApiService _shikiApiService;

    public AnimeMetadataViewModel(
        AnimeEntity anime,
        Kiriha.Core.Services.IMalApiService malApiService,
        Kiriha.Core.Services.IShikiApiService shikiApiService)
    {
        _anime = anime;
        _malApiService = malApiService;
        _shikiApiService = shikiApiService;
    }

    public string JoinedGenres => string.Join(", ", _anime.Genres);
    public string JoinedStudios => string.Join(", ", _anime.Studios);
    public string JoinedAltTitles => string.Join(", ", _anime.AlternativeTitles);

    private IEnumerable<string>? _allAlternativeTitles;
    public IEnumerable<string> AllAlternativeTitles
    {
        get
        {
            if (_allAlternativeTitles != null)
                return _allAlternativeTitles;

            var list = new List<string>();
            if (!string.IsNullOrEmpty(_anime.EnglishTitle) && _anime.EnglishTitle != _anime.Title)
                list.Add(_anime.EnglishTitle);
            if (!string.IsNullOrEmpty(_anime.JapaneseTitle) && _anime.JapaneseTitle != _anime.Title)
                list.Add(_anime.JapaneseTitle);

            foreach (var syn in _anime.AlternativeTitles)
            {
                if (syn != _anime.Title && !list.Contains(syn))
                    list.Add(syn);
            }

            return _allAlternativeTitles = list;
        }
    }

    public bool HasAlternativeTitles => AllAlternativeTitles.Any();

    private string? _combinedAltTitles;
    public string CombinedAltTitles
    {
        get
        {
            if (_combinedAltTitles != null)
                return _combinedAltTitles;

            var list = new List<string>();
            if (!string.IsNullOrEmpty(_anime.RussianTitle)) list.Add(_anime.RussianTitle);
            if (!string.IsNullOrEmpty(_anime.EnglishTitle)) list.Add(_anime.EnglishTitle);
            if (!string.IsNullOrEmpty(_anime.JapaneseTitle)) list.Add(_anime.JapaneseTitle);
            list.AddRange(_anime.AlternativeTitles);

            return _combinedAltTitles = string.Join(", ", list.Distinct());
        }
    }

    public void NotifyMetadataChanged()
    {
        _allAlternativeTitles = null;
        _combinedAltTitles = null;
        OnPropertyChanged(nameof(JoinedGenres));
        OnPropertyChanged(nameof(JoinedStudios));
        OnPropertyChanged(nameof(JoinedAltTitles));
        OnPropertyChanged(nameof(CombinedAltTitles));
        OnPropertyChanged(nameof(HasAlternativeTitles));
        OnPropertyChanged(nameof(AllAlternativeTitles));
    }

    public async Task LoadFullDetailsAsync()
    {
        var full = _anime.Presentation.IsManga
            ? await _malApiService.GetMangaDetailsAsync(_anime.Id)
            : await _malApiService.GetAnimeDetailsAsync(_anime.Id);

        if (full != null)
        {
            if (full.Status != UserAnimeStatus.None) _anime.Status = full.Status;
            _anime.Progress = full.Progress;
            _anime.Score = full.Score;
            _anime.Notes = full.Notes;
            _anime.RewatchCount = full.RewatchCount;
            _anime.IsRewatching = full.IsRewatching;
            _anime.DateStarted = full.DateStarted;
            _anime.DateCompleted = full.DateCompleted;

            if (!string.IsNullOrEmpty(full.Synopsis)) _anime.Synopsis = full.Synopsis;

            if (full.Genres.Count > 0)
            {
                _anime.Genres.Clear();
                foreach (var g in full.Genres) _anime.Genres.Add(g);
            }

            if (full.Studios.Count > 0)
            {
                _anime.Studios.Clear();
                foreach (var s in full.Studios) _anime.Studios.Add(s);
            }

            if (full.AlternativeTitles.Count > 0)
            {
                foreach (var t in full.AlternativeTitles)
                {
                    if (!_anime.AlternativeTitles.Contains(t))
                        _anime.AlternativeTitles.Add(t);
                }
            }

            _anime.EnglishTitle = full.EnglishTitle;
            _anime.JapaneseTitle = full.JapaneseTitle;
            _anime.StatusDetailed = full.StatusDetailed;
            _anime.MeanScore = full.MeanScore;
            _anime.Popularity = full.Popularity;
            _anime.Rank = full.Rank;
            _anime.AiringDate = full.AiringDate;
            _anime.StartSeason = full.StartSeason;
            _anime.StartYear = full.StartYear;

            if (!string.IsNullOrEmpty(full.MainPictureUrl)) _anime.MainPictureUrl = full.MainPictureUrl;
            if (!string.IsNullOrEmpty(full.LocalPosterPath)) _anime.LocalPosterPath = full.LocalPosterPath;

            _anime.Season = full.Season;
            _anime.RefreshMetadata();
            
            NotifyMetadataChanged();
        }
    }
}
