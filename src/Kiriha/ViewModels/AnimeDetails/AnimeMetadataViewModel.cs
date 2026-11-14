using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.ViewModels.AnimeDetails;

public partial class AnimeMetadataViewModel : ObservableObject
{
    private readonly AnimeEntity _anime;
    private readonly IMalApiService _malApiService;

    public AnimeMetadataViewModel(
        AnimeEntity anime,
        IMalApiService malApiService)
    {
        _anime = anime;
        _malApiService = malApiService;
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

    public async Task<AnimeEntity?> LoadFullDetailsAsync()
    {
        var full = _anime.Presentation.IsManga
            ? await _malApiService.GetMangaDetailsAsync(_anime.Id)
            : await _malApiService.GetAnimeDetailsAsync(_anime.Id);

        if (full != null)
        {
            _anime.MergeFullDetails(full);
            _anime.RefreshMetadata();
            NotifyMetadataChanged();
        }

        return full;
    }
}
