using MaplibreNative.Routing.Core.Models;

namespace MaplibreNative.Routing.Core.Routing;

/// <summary>Contract for all routing engines.</summary>
public interface IRoutingEngine
{
    Task<DirectionsRoute?> RouteAsync(RouteOptions options);

    /// <summary>Compute up to <paramref name="maxCount"/> distinct route alternatives, sorted
    /// shortest first. The default implementation wraps <see cref="RouteAsync"/> and returns
    /// at most one route; override in engines that can generate genuine alternatives (e.g.
    /// <c>HybridRouter</c> runs penalised A* for each additional alternative).</summary>
    async Task<List<DirectionsRoute>> FindAlternativesAsync(RouteOptions options, int maxCount)
    {
        var route = await RouteAsync(options);
        return route is null ? [] : [route];
    }
}
