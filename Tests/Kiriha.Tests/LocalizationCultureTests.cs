using System;
using System.Globalization;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data.Metadata;
using Kiriha.ViewModels.AnimeList;
using Moq;
using Xunit;

namespace Kiriha.Tests;

public class LocalizationCultureTests
{
    private sealed class StubLocalizer : ILocalizer
    {
        public CultureInfo CurrentCulture { get; set; } = CultureInfo.InvariantCulture;
        public string GetLoc(string key) => key;
        public string GetLoc(string key, params object?[] args) => key;
    }

    [Fact]
    public void AiringDateDisplay_WithRussianCulture_FormatsRussianMonth()
    {
        var airingDate = new DateTime(2026, 9, 5);
        var item = new AnimeEntity
        {
            Id = 1,
            Title = "Test Anime",
            AiringDate = airingDate
        };

        var ruCulture = CultureInfo.GetCultureInfo("ru-RU");
        var localizer = new StubLocalizer { CurrentCulture = ruCulture };
        var pres = new AnimeEntityPresentation(item, localizer);

        var expected = airingDate.ToString("d MMM yyyy", ruCulture);
        Assert.Equal(expected, pres.AiringDateDisplay);
        Assert.DoesNotContain("Sep", pres.AiringDateDisplay, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AiringDateDisplay_WithEnglishCulture_FormatsEnglishMonth()
    {
        var airingDate = new DateTime(2026, 9, 5);
        var item = new AnimeEntity
        {
            Id = 1,
            Title = "Test Anime",
            AiringDate = airingDate
        };

        var enCulture = CultureInfo.GetCultureInfo("en-US");
        var localizer = new StubLocalizer { CurrentCulture = enCulture };
        var pres = new AnimeEntityPresentation(item, localizer);

        Assert.Equal("5 Sep 2026", pres.AiringDateDisplay);
    }

    [Fact]
    public void GetCultureForLanguage_ReturnsCorrectCulture()
    {
        Assert.Equal("ru-RU", LocalizationService.GetCultureForLanguage(AppConstants.Languages.Ru).Name);
        Assert.Equal("en-US", LocalizationService.GetCultureForLanguage(AppConstants.Languages.En).Name);
        Assert.Equal("en-US", LocalizationService.GetCultureForLanguage(null).Name);
    }

    [Fact]
    public void ReleaseMapViewModel_GetReleaseCulture_RespectsCulture()
    {
        var prevUi = CultureInfo.CurrentUICulture;
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

            var culture = ReleaseMapViewModel.GetReleaseCulture();
            Assert.Equal("ru-RU", culture.Name);

            var releaseDate = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Local);
            var formatted = ReleaseMapViewModel.FormatMonthShort(releaseDate);
            var expectedMonth = releaseDate.ToString("MMM", culture).TrimEnd('.').ToUpper(culture);
            Assert.Equal(expectedMonth, formatted);
        }
        finally
        {
            CultureInfo.CurrentUICulture = prevUi;
            CultureInfo.CurrentCulture = prev;
        }
    }
}
