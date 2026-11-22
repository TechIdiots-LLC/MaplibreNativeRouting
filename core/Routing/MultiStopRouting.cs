using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Routing;

/// <summary>
/// A route through stops (<see cref="RouteOptions.Waypoints"/>) with any engine: a leg from the origin to each stop in
/// turn and on to the destination, each routed on its own, joined into one route of several legs.
/// </summary>
public static class MultiStopRouting
{
    /// <summary>The route through every stop, or null when a leg of it can't be found.</summary>
    public static async Task<DirectionsRoute?> RouteAsync(IRoutingEngine engine, RouteOptions options)
    {
        if (options.Waypoints.Count == 0) return await engine.RouteAsync(options).ConfigureAwait(false);
        var points = new List<(double Lat, double Lon)> { options.Origin };
        points.AddRange(options.Waypoints);
        points.Add(options.Destination);

        var legs = new List<RouteLeg>();
        for (int i = 0; i + 1 < points.Count; i++)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            options.Progress?.Report($"Routing leg {i + 1} of {points.Count - 1}…");
            var part = await engine.RouteAsync(options with { Origin = points[i], Destination = points[i + 1], Waypoints = [] })
                .ConfigureAwait(false);
            if (part is null || part.Legs.Count == 0) return null;
            legs.AddRange(part.Legs);
        }
        return RouteUtils.AttachHighwayWarning(new DirectionsRoute
        {
            Distance = legs.Sum(l => l.Distance),
            Duration = legs.Sum(l => l.Duration),
            Legs = legs,
            Profile = options.Profile,
        });
    }
}
