using System.Threading.Tasks;

namespace Kiriha.Core.Services;

public interface IDatabaseInitializer
{
    Task InitializationTask { get; }
}