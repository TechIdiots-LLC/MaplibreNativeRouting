using MaplibreNative.Routing.Core.Graph;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Routing;

/// <summary>
/// Combines road routing with trail routing (SpatialGraph A*).
///
/// Flow:
///   1. Build SpatialGraph from track features (once).
///   2. Find nearest track nodes to origin and destination.
///   3. Run A* on the already-built graph (once).
///   3a. If both snap within SnapThresholdM and A* succeeds → return pure trail route.
///   3b. Otherwise attempt hybrid stitch:
///       - Road engine: origin → nearest track entry point   (only when origin is far from trail)
///       - Pre-computed A* result: entry → exit
///       - Road engine: track exit → destination             (only when dest is far from trail)
///       - Stitch into a single DirectionsRoute.
///   4. Attach HighwayWarning if any road segment uses motorway/trunk.
/// </summary>
public class HybridRouter : IRoutingEngine
{
    private const double SnapThresholdM = 200; // within 200 m → consider on-track

    private readonly IRoutingEngine _roadEngine;

    public HybridRouter() : this(new ValhallaMtbRouter()) { }

    public HybridRouter(IRoutingEngine roadEngine)
    {
        _roadEngine = roadEngine;
    }

    public async Task<DirectionsRoute?> RouteAsync(RouteOptions options)
    {
        var progress = options.Progress;

        if (options.TrackFeatures.Count == 0)
        {
            progress?.Report("No trail features — routing by road only…");
            return await _roadEngine.RouteAsync(options);
        }

        progress?.Report("Building trail graph…");
        var graph = SpatialGraph.Build(options.TrackFeatures);
        progress?.Report(
            $"Trail graph: {graph.Nodes.Count:N0} nodes, {graph.EdgeCount:N0} edges" +
            $" from {options.TrackFeatures.Count:N0} features");

        if (graph.Nodes.Count == 0)
        {
            progress?.Report("No trail graph — routing by road only…");
            return await _roadEngine.RouteAsync(options);
        }

        var originNode = graph.NearestNode(options.Origin.Lat, options.Origin.Lon);
        var destNode   = graph.NearestNode(options.Destination.Lat, options.Destination.Lon);
        if (originNode is null || destNode is null)
        {
            progress?.Report("Could not snap to trail — routing by road only…");
            return await _roadEngine.RouteAsync(options);
        }

        double originSnap = RouteUtils.HaversineMeters(
            options.Origin.Lat, options.Origin.Lon, originNode.Lat, originNode.Lon);
        double destSnap = RouteUtils.HaversineMeters(
            options.Destination.Lat, options.Destination.Lon, destNode.Lat, destNode.Lon);

        progress?.Report($"Origin {originSnap:F0} m from trail, dest {destSnap:F0} m from trail");

        // Run trail A* once on the already-built graph.
        progress?.Report("Routing on trail…");
        var trailPath    = AStarSolver.FindPath(graph, originNode, destNode);
        var trailSegment = trailPath is { Count: >= 2 }
            ? BuildTrailRoute(trailPath, options.Profile)
            : null;

        // Trail A* found a path → return it directly, regardless of snap distances.
        // The route may start/end a short distance from the pins (at the nearest trail node),
        // which is acceptable. Don't discard a valid trail route just because an endpoint is
        // slightly outside the snap threshold — the hybrid road stitch for those extra meters
        // fails anyway when the trail node is forest-only with no road access in the MVT data.
        if (trailSegment is not null)
        {
            progress?.Report($"Trail route found ({trailSegment.Distance / 1000.0:F1} km).");
            return trailSegment;
        }

        progress?.Report("Trail A* found no path — network may be disconnected between these locations");

        // Trail A* failed. Hybrid stitch: road-to-trail (when origin is far from trail),
        // then road-to-destination (when dest is far from trail). The trail segment itself
        // is skipped here because it uses the same graph and would fail identically.
        var entryPoint = (originNode.Lat, originNode.Lon);
        var exitPoint  = (destNode.Lat, destNode.Lon);

        DirectionsRoute? roadToTrail = null;
        if (originSnap > SnapThresholdM)
        {
            progress?.Report("Routing road → trail entry…");
            roadToTrail = await _roadEngine.RouteAsync(options with { Destination = entryPoint });
        }

        DirectionsRoute? trailToRoad = null;
        if (destSnap > SnapThresholdM)
        {
            progress?.Report("Routing trail exit → road…");
            trailToRoad = await _roadEngine.RouteAsync(options with { Origin = exitPoint });
        }

        progress?.Report("Stitching route…");
        return Stitch(roadToTrail, trailSegment, trailToRoad, options.Profile);
    }

    private static DirectionsRoute? Stitch(
        DirectionsRoute? roadIn,
        DirectionsRoute? trail,
        DirectionsRoute? roadOut,
        RouteProfile profile)
    {
        // Require at minimum the trail segment.
        if (trail is null) return roadIn ?? roadOut;

        var allLegs = new List<RouteLeg>();
        double totalDist = 0, totalDur = 0;

        void AddLegs(DirectionsRoute? r)
        {
            if (r is null) return;
            allLegs.AddRange(r.Legs);
            totalDist += r.Distance;
            totalDur  += r.Duration;
        }

        AddLegs(roadIn);
        AddLegs(trail);
        AddLegs(roadOut);

        var stitched = new DirectionsRoute
        {
            Distance = totalDist,
            Duration = totalDur,
            Legs     = allLegs,
            Profile  = profile,
        };

        return RouteUtils.AttachHighwayWarning(stitched);
    }

    private static DirectionsRoute BuildTrailRoute(List<GraphNode> path, RouteProfile profile)
    {
        var shape = path.Select(n => (n.Lon, n.Lat)).ToList();
        double totalDist = 0;
        for (int i = 1; i < path.Count; i++)
            totalDist += RouteUtils.HaversineMeters(
                path[i - 1].Lat, path[i - 1].Lon,
                path[i].Lat,     path[i].Lon);

        var steps = new List<LegStep>
        {
            new()
            {
                Type             = ManeuverType.Start,
                Instruction      = "Follow the trail",
                Distance         = totalDist,
                Duration         = totalDist / 5.5, // ~20 km/h
                BeginShapeIndex  = 0,
                EndShapeIndex    = shape.Count - 1,
                ManeuverLocation = shape[0],
            },
            new()
            {
                Type             = ManeuverType.Destination,
                Instruction      = "Arrive at destination",
                BeginShapeIndex  = shape.Count - 1,
                EndShapeIndex    = shape.Count - 1,
                ManeuverLocation = shape[^1],
            },
        };

        var leg = new RouteLeg
        {
            Distance = totalDist,
            Duration = totalDist / 5.5,
            Summary  = "Trail route",
            Steps    = steps,
            Shape    = shape,
        };

        return new DirectionsRoute
        {
            Distance = totalDist,
            Duration = totalDist / 5.5,
            Legs     = [leg],
            Profile  = profile,
        };
    }
}
