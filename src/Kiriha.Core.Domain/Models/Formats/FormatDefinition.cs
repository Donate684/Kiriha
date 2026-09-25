namespace Kiriha.Core.Domain.Models.Formats;

public sealed record FormatDefinition(
    string Key,
    string EnglishName,
    string RussianName,
    string[]? Aliases = null)
{
    public string DisplayName => RussianName;

    public string FullDisplay => $"{RussianName} ({EnglishName})";

    public override string ToString() => RussianName;
}
