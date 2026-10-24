using Kiriha.Core.Domain.Models;

namespace Kiriha.Core.Abstractions.Services;

public interface IExternalMediaDetector
{
    event EventHandler<ParsedMedia>? MediaDetected;
    event EventHandler? MediaCleared;
    event EventHandler<IReadOnlySet<string>>? RunningPlayersChanged;

    List<ExternalPlayerDefinition> AvailablePlayers { get; }
    IReadOnlySet<string> RunningPlayerNames { get; }
}
