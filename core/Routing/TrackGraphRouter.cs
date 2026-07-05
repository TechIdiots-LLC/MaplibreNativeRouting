using MaplibreNative.Routing.Core.Graph;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Routing;

/// <summary>Routes purely on user-imported track features via A* on a SpatialGraph.
/// Used for TrackOnly profile (snowmobile, off-road-only) and as the trail segment
/// in HybridRouter.</summary>
public class TrackGraphRouter : IRoutingEngine
{
    public Task<DirectionsRoute?> RouteAsync(RouteOptions options)
    {
        if (options.TrackFeatures.Count == 0)
            return Task.FromResult<DirectionsRoute?>(null);

        var graph = SpatialGraph.Build(options.TrackFeatures);
        if (graph.Nodes.Count == 0)
            return Task.FromResult<DirectionsRoute?>(null);

        var startNode = graph.NearestNode(options.Origin.Lat, options.Origin.Lon);
        var goalNode = graph.NearestNode(options.Destination.Lat, options.Destination.Lon);
        if (startNode is null || goalNode is null)
            return Task.FromResult<DirectionsRoute?>(null);

        var path = AStarSolver.FindPath(graph, startNode, goalNode);
        if (path is null || path.Count < 2)
            return Task.FromResult<DirectionsRoute?>(null);

        return Task.FromResult<DirectionsRoute?>(BuildRoute(path, options.Profile));
    }

    private static DirectionsRoute BuildRoute(List<GraphNode> path, RouteProfile profile)
    {
        var shape = path.Select(n => (n.Lon, n.Lat)).ToList();
        double totalDist = 0;
        for (int i = 1; i < path.Count; i++)
            totalDist += RouteUtils.HaversineMeters(path[i - 1].Lat, path[i - 1].Lon,
                                                    path[i].Lat, path[i].Lon);

        var steps = new List<LegStep>
        {
            new()
            {
                Type = ManeuverType.Start,
                Instruction = "Follow the trail",
                Distance = totalDist,
                Duration = totalDist / 5.5, // ~20 km/h estimate
                BeginShapeIndex = 0,
                EndShapeIndex = shape.Count - 1,
                ManeuverLocation = shape[0],
            },
            new()
            {
                Type = ManeuverType.Destination,
                Instruction = "Arrive at destination",
                BeginShapeIndex = shape.Count - 1,
                EndShapeIndex = shape.Count - 1,
                ManeuverLocation = shape[^1],
            },
        };

        var leg = new RouteLeg
        {
            Distance = totalDist,
            Duration = totalDist / 5.5,
            Summary = "Trail route",
            Steps = steps,
            Shape = shape,
        };

        return new DirectionsRoute
        {
            Distance = totalDist,
            Duration = totalDist / 5.5,
            Legs = [leg],
            Profile = profile,
        };
    }
}
