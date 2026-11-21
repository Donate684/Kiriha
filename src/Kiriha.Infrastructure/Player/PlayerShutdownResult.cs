namespace Kiriha.Infrastructure.Player;

public enum PlayerShutdownResult : byte
{
    Unavailable = 0,
    Completed = 1,
    Failed = 2,
    Unconfirmed = 3
}
