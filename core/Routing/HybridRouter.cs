using MaplibreNative.Routing.Core.Graph;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Routing;

/// <summary>
/// Combines road routing with trail routing (TrackGraphRouter/A*).
///
/// Flow:
///   1. Build SpatialGraph from track features.
///   2. Find nearest track nodes to origin and destination.
///   3a. If both snap within SnapThresholdM → pure TrackGraphRouter.
///   3b. Otherwise:
///       - Road engine: origin → nearest track entry point
///       - A*: track entry → track exit
///       - Road engine: track exit → destination
///       - Stitch into a single DirectionsRoute.
///   4. Attach HighwayWarning if any road segment uses motorway/trunk.
/// </summary>
public class HybridRouter : IRoutingEngine
{
    private const double SnapThresholdM = 200; // within 200 m → consider on-track

    private readonly IRoutingEngine _roadEngine;
    private readonly TrackGraphRouter _track = new();

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
        progress?.Report($"Trail graph: {graph.Nodes.Count:N0} nodes from {options.TrackFeatures.Count:N0} features");
        if (graph.Nodes.Count == 0)
        {
            progress?.Report("No trail graph — routing by road only…");
            return await _roadEngine.RouteAsync(options);
        }

        var originNode = graph.NearestNode(options.Origin.Lat, options.Origin.Lon);
        var destNode = graph.NearestNode(options.Destination.Lat, options.Destination.Lon);
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

        // Both ends snap to the track network → try pure trail route first.
        if (originSnap <= SnapThresholdM && destSnap <= SnapThresholdM)
        {
            progress?.Report("Routing on trail…");
            var trailOnly = await _track.RouteAsync(options);
            if (trailOnly is not null)
                return trailOnly;
            // Trail A* returned null — graph is disconnected between these nodes.
            // Fall through to hybrid stitch so road segments can bridge the gap.
            progress?.Report("Trail path disconnected — trying hybrid route…");
        }

        // Hybrid stitch: road to nearest trail entry, A* on trail, road to destination.
        var entryPoint = (originNode.Lat, originNode.Lon);
        var exitPoint = (destNode.Lat, destNode.Lon);

        progress?.Report("Routing road → trail entry…");
        var roadToTrail = await _roadEngine.RouteAsync(options with { Destination = entryPoint });

        progress?.Report("Routing on trail…");
        var trailSegment = await _track.RouteAsync(options with
        {
            Origin = entryPoint,
            Destination = exitPoint,
        });

        progress?.Report("Routing trail exit → road…");
        var trailToRoad = await _roadEngine.RouteAsync(options with { Origin = exitPoint });

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
            totalDur += r.Duration;
        }

        AddLegs(roadIn);
        AddLegs(trail);
        AddLegs(roadOut);

        var stitched = new DirectionsRoute
        {
            Distance = totalDist,
            Duration = totalDur,
            Legs = allLegs,
            Profile = profile,
        };

        return RouteUtils.AttachHighwayWarning(stitched);
    }
}
