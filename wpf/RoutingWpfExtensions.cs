using Microsoft.Extensions.DependencyInjection;

namespace MaplibreNative.Routing.Wpf;

public static class RoutingWpfExtensions
{
    /// <summary>Registers MaplibreRouting WPF services with a DI container.
    /// Call from your app startup: services.AddMaplibreRouting().</summary>
    public static IServiceCollection AddMaplibreRouting(
        this IServiceCollection services,
        IRouteDataSource? dataSource = null)
    {
        if (dataSource is not null)
            services.AddSingleton(dataSource);

        services.AddSingleton<RouteOverlay>();
        services.AddTransient<NavigationSession>();
        return services;
    }
}
