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

    public void Refresh(IReadOnlyCollection<AnimeEntity> items, IReadOnlyCollection<AnimeEntity> nonPlanned)
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


}

