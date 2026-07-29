using System.Threading.Tasks;

namespace Kiriha.Services.AppLifecycle.Shutdown;

public interface IShutdownHandler
{
    Task FlushAsync();
}
