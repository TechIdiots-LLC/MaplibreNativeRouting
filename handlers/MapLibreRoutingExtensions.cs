using MaplibreNative.Routing.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace MaplibreNative.Routing;

public static class MapLibreRoutingExtensions
{
    /// <summary>Registers MapLibreRouting services and controls with the MAUI DI container.
    /// Call from MauiProgram.cs: builder.UseMapLibreRouting().</summary>
    public static MauiAppBuilder UseMapLibreRouting(
        this MauiAppBuilder builder,
        IRouteDataSource? dataSource = null)
    {
        if (dataSource is not null)
            builder.Services.AddSingleton(dataSource);

        builder.Services.AddSingleton<RouteOverlay>();
        builder.Services.AddTransient<NavigationSession>();

        return builder;
    }
}
