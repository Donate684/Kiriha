namespace Kiriha.Core.Abstractions.Services;

public interface IDatabaseInitializer
{
    Task InitializationTask { get; }
    Task InitializeAsync() => InitializationTask;
    Task FlushAsync() => Task.CompletedTask;
}
