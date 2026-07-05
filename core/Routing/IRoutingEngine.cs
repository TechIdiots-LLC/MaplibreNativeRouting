using MaplibreNative.Routing.Core.Models;

namespace MaplibreNative.Routing.Core.Routing;

/// <summary>Contract for all routing engines. Returns a single best route or null
/// if no route could be found.</summary>
public interface IRoutingEngine
{
    Task<DirectionsRoute?> RouteAsync(RouteOptions options);
}
