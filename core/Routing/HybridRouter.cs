using MaplibreNative.Routing.Core.Graph;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Routing;

/// <summary>
/// Combines road routing (Valhalla) with trail routing (TrackGraphRouter/A*) for
/// HybridMotorcycle and HybridBicycle profiles.
///
/// Flow:
///   1. Build SpatialGraph from track features.
///   2. Find nearest track nodes to origin and destination.
///   3a. If both snap within SnapThresholdM → pure TrackGraphRouter.
///   3b. Otherwise:
///       - Valhalla: origin → nearest track entry point
///       - A*: track entry → track exit
///       - Valhalla: track exit → destination
///       - Stitch into a single DirectionsRoute.
///   4. Attach HighwayWarning if any road segment uses motorway/trunk.
/// </summary>
public class HybridRouter : IRoutingEngine
{
    private const double SnapThresholdM = 200; // within 200 m → consider on-track

    private readonly ValhallaMtbRouter _valhalla = new();
    private readonly TrackGraphRouter _track = new();

    public async Task<DirectionsRoute?> RouteAsync(RouteOptions options)
    {
        if (options.TrackFeatures.Count == 0)
            return await _valhalla.RouteAsync(options);

        var graph = SpatialGraph.Build(options.TrackFeatures);
        if (graph.Nodes.Count == 0)
            return await _valhalla.RouteAsync(options);

        var originNode = graph.NearestNode(options.Origin.Lat, options.Origin.Lon);
        var destNode = graph.NearestNode(options.Destination.Lat, options.Destination.Lon);
        if (originNode is null || destNode is null)
            return await _valhalla.RouteAsync(options);

        double originSnap = RouteUtils.HaversineMeters(
            options.Origin.Lat, options.Origin.Lon, originNode.Lat, originNode.Lon);
        double destSnap = RouteUtils.HaversineMeters(
            options.Destination.Lat, options.Destination.Lon, destNode.Lat, destNode.Lon);

        // Both ends snap to the track network → pure trail route.
        if (originSnap <= SnapThresholdM && destSnap <= SnapThresholdM)
            return await _track.RouteAsync(options);

        // Partial or no track coverage → hybrid stitch.
        var entryPoint = (originNode.Lat, originNode.Lon);
        var exitPoint = (destNode.Lat, destNode.Lon);

        var roadToTrail = await _valhalla.RouteAsync(options with { Destination = entryPoint });
        var trailSegment = await _track.RouteAsync(options with
        {
            Origin = entryPoint,
            Destination = exitPoint,
        });
        var trailToRoad = await _valhalla.RouteAsync(options with { Origin = exitPoint });

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
