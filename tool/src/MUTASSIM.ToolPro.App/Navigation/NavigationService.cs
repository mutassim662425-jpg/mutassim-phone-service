using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MUTASSIM.ToolPro.App.Navigation;

/// <summary>Implemented by page view models that need to load data when shown.</summary>
public interface IPageViewModel
{
    Task OnNavigatedToAsync();
}

/// <summary>
/// Route-based navigation. Every page is created fresh (transient) and disposed when left,
/// so pages never keep stale data or event subscriptions.
/// </summary>
public sealed partial class NavigationService(IServiceProvider services, ILogger<NavigationService> logger)
    : ObservableObject
{
    private readonly Dictionary<string, Func<IServiceProvider, object>> _routes = new(StringComparer.Ordinal);
    private IServiceScope? _scope;

    [ObservableProperty] private object? _currentViewModel;
    [ObservableProperty] private string? _currentRoute;

    public event EventHandler<string>? Navigated;

    public void Register(string route, Func<IServiceProvider, object> factory) => _routes[route] = factory;

    public bool CanNavigate(string route) => _routes.ContainsKey(route);

    public void Navigate(string route)
    {
        if (!_routes.TryGetValue(route, out var factory))
        {
            logger.LogWarning("Unknown route {Route}", route);
            return;
        }

        var previous = CurrentViewModel;
        var previousScope = _scope;

        // One DI scope per page: everything the page resolved is released when the page is left.
        _scope = services.CreateScope();
        var next = factory(_scope.ServiceProvider);

        CurrentRoute = route;
        CurrentViewModel = next;
        (previous as IDisposable)?.Dispose(); // pages created via ActivatorUtilities are not tracked by the scope
        previousScope?.Dispose();

        if (next is IPageViewModel page) _ = LoadPageAsync(page, route);
        Navigated?.Invoke(this, route);
    }

    /// <summary>Re-creates the current page (used after a language change).</summary>
    public void Reload()
    {
        if (CurrentRoute is { } route) Navigate(route);
    }

    private async Task LoadPageAsync(IPageViewModel page, string route)
    {
        try
        {
            await page.OnNavigatedToAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Page {Route} failed to load", route);
        }
    }
}
