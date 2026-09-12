namespace Kiriha.Core.Abstractions.Services;

public interface ILocalizer
{
    string GetLoc(string key);
    string GetLoc(string key, params object?[] args);
}
