using System.Threading.Tasks;

namespace Kiriha.Core.Abstractions.Services;

public interface IDatabaseInitializer
{
    Task InitializationTask { get; }
}