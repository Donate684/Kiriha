namespace Kiriha.Core.Abstractions.Infrastructure;

public interface IUiDispatcher
{
    bool HasThreadAffinity => false;
    bool CheckAccess() => true;
    Task InvokeAsync(Action action);
    Task<T> InvokeAsync<T>(Func<T> action);
    Task InvokeAsync(Func<Task> action);
    void Post(Action action);
}
