using MaplibreNative.Routing.Core.Graph;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Routing;

/// <summary>
/// Combines a road service (Valhalla) with trail routing (SpatialGraph A*), for the online hybrid profiles. The
/// offline ones route trails and roads as one network instead (<see cref="HybridGraphRouter"/>).
///
/// Flow:
///   1. Build SpatialGraph from track features, less any closures.
///   2. Find nearest track nodes to origin and destination, and A* between them.
///   3. When the trails connect them: the road from the origin to the trail when the origin is off it
///      (beyond SnapThresholdM), the trail, and the road from the trail to the destination when that's off it.
///   4. When they don't: the road route from the origin to the destination.
///   5. Attach HighwayWarning if any road segment uses motorway/trunk.
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
        var graph = SpatialGraph.Build(RouteClosure.Apply(options.TrackFeatures, options.Closures));
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

        // The trails don't connect the two: the road all the way
        if (trailSegment is null)
        {
            progress?.Report("The trails don't connect these places — routing by road…");
            return await _roadEngine.RouteAsync(options);
        }
        progress?.Report($"Trail route found ({trailSegment.Distance / 1000.0:F1} km).");

        // On the trail at both ends: the trail alone
        DirectionsRoute? roadToTrail = null, trailToRoad = null;
        if (originSnap > SnapThresholdM)
        {
            progress?.Report("Routing road → trail entry…");
            roadToTrail = await _roadEngine.RouteAsync(options with { Destination = (originNode.Lat, originNode.Lon) });
        }
        if (destSnap > SnapThresholdM)
        {
            progress?.Report("Routing trail exit → road…");
            trailToRoad = await _roadEngine.RouteAsync(options with { Origin = (destNode.Lat, destNode.Lon) });
        }

        progress?.Report("Stitching route…");
        return Stitch(roadToTrail, trailSegment, trailToRoad, options.Profile);
    }

    /// <summary>
    /// Computes up to <paramref name="maxCount"/> distinct trail routes between the same two
    /// endpoints by re-running A* with progressively penalised edges from previous paths.
    /// Results are sorted shortest-distance first. Falls back to a single <see cref="RouteAsync"/>
    /// result if trail A* finds no path at all.
    /// </summary>
    public async Task<List<DirectionsRoute>> FindAlternativesAsync(RouteOptions options, int maxCount)
    {
        if (options.TrackFeatures.Count == 0 || maxCount <= 0)
            goto fallback;

        var graph = SpatialGraph.Build(RouteClosure.Apply(options.TrackFeatures, options.Closures));
        if (graph.Nodes.Count == 0) goto fallback;

        var originNode = graph.NearestNode(options.Origin.Lat, options.Origin.Lon);
        var destNode   = graph.NearestNode(options.Destination.Lat, options.Destination.Lon);
        if (originNode is null || destNode is null) goto fallback;

        var results = new List<DirectionsRoute>();
        HashSet<int>? penalized = null;

        for (int attempt = 0; attempt < Math.Min(maxCount, 5); attempt++)
        {
            var path = AStarSolver.FindPath(graph, originNode, destNode, penalized, penalty: 3.0);
            if (path is null || path.Count < 2) break;

            var route = BuildTrailRoute(path, options.Profile);

            // Skip near-duplicate routes (within 1 km of a result already in the list).
            if (results.Any(r => Math.Abs(r.Distance - route.Distance) < 1_000)) break;

            results.Add(route);

            // Penalise nodes from this path so the next run is forced onto a different corridor.
            var pathIds = new HashSet<int>(path.Select(n => n.Id));
            penalized = penalized is null ? pathIds : new HashSet<int>(penalized.Concat(pathIds));
        }

        if (results.Count > 0)
        {
            // Return shortest first so the caller can default to results[0].
            results.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return results;
        }

        fallback:
        var fallbackRoute = await RouteAsync(options);
        return fallbackRoute is null ? [] : [fallbackRoute];
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
