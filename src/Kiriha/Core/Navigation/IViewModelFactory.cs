using Kiriha.ViewModels;
using Kiriha.ViewModels.Search;
using Kiriha.ViewModels.Startup;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Core.Navigation;

/// <summary>
/// Resolves transient ViewModels for navigation. Replaces the previous
/// <c>Func&lt;TViewModel&gt;</c> service-locator factories registered in
/// <c>App.ConfigureServices</c> — those bypassed the type system and forced
/// every "I need a fresh VM" site to add a parameter to the consumer's ctor.
///
/// For singletons, keep using direct constructor injection. This factory is
/// strictly for transient ViewModels (e.g. <see cref="WelcomeViewModel"/>,
/// <see cref="SearchViewModel"/>) that must be re-created on every navigation
/// to clear per-page state.
/// </summary>
public interface IViewModelFactory
{
    TViewModel Create<TViewModel>() where TViewModel : ViewModelBase;
    TViewModel CreateWithArgs<TViewModel>(params object[] args) where TViewModel : ViewModelBase;
}

/// <summary>
/// Default implementation backed by <see cref="IServiceProvider"/>. The
/// transient/singleton lifetime decision lives in DI registrations, so this
/// class is intentionally a thin pass-through.
/// </summary>
public sealed class ViewModelFactory : IViewModelFactory
{
    private readonly IServiceProvider _services;
    private readonly IReadOnlySet<Type>? _transientTypes;

    public ViewModelFactory(IServiceProvider services, IReadOnlySet<Type>? transientTypes = null)
    {
        _services = services;
        _transientTypes = transientTypes;
    }

    public TViewModel Create<TViewModel>() where TViewModel : ViewModelBase
    {
        // Avoid resolving transient IDisposable ViewModels via root IServiceProvider, which
        // retains strong references to every transient IDisposable until application exit.
        if (_transientTypes != null && _transientTypes.Contains(typeof(TViewModel)))
        {
            return ActivatorUtilities.CreateInstance<TViewModel>(_services);
        }

        return _services.GetRequiredService<TViewModel>();
    }

    public TViewModel CreateWithArgs<TViewModel>(params object[] args) where TViewModel : ViewModelBase =>
        ActivatorUtilities.CreateInstance<TViewModel>(_services, args);
}
