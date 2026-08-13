namespace Kiriha.Core.Abstractions.Services;

public interface IHistoryService
{
    void AddEntry(int animeId, string title, string? russianTitle, int episode, string actionType = "Watched", object? detail = null);
}
