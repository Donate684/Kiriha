using System.Globalization;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Services.Metadata;

namespace Kiriha.Models;

public record RatingOption(string Value, string Description)
{
    public string Display => Description != "" ? $"{Value} {Description}" : Value;
    public override string ToString() => Display;
}

public static class RatingHelper
{
    public static RatingOption GetRatingOption(string? scoreStr, ILocalizer? localizer = null)
    {
        if (string.IsNullOrEmpty(scoreStr) || scoreStr == "-")
            return new RatingOption("-", "");

        var loc = localizer ?? LocalizationService.Active;
        if (double.TryParse(scoreStr.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double score))
        {
            if (score <= 0) return new RatingOption("-", "");

            return score switch
            {
                >= 10 => new RatingOption("10", $"({loc.GetLoc("filters.score.masterpiece")})"),
                >= 9 => new RatingOption("9", $"({loc.GetLoc("filters.score.amazing")})"),
                >= 8 => new RatingOption("8", $"({loc.GetLoc("filters.score.great")})"),
                >= 7 => new RatingOption("7", $"({loc.GetLoc("filters.score.good")})"),
                >= 6 => new RatingOption("6", $"({loc.GetLoc("filters.score.fine")})"),
                >= 5 => new RatingOption("5", $"({loc.GetLoc("filters.score.average")})"),
                >= 4 => new RatingOption("4", $"({loc.GetLoc("filters.score.weak")})"),
                >= 3 => new RatingOption("3", $"({loc.GetLoc("filters.score.bad")})"),
                >= 2 => new RatingOption("2", $"({loc.GetLoc("filters.score.terrible")})"),
                >= 1 => new RatingOption("1", $"({loc.GetLoc("filters.score.bottom")})"),
                _ => new RatingOption("-", "")
            };
        }

        return new RatingOption("-", "");
    }
}
