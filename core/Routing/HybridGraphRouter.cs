using MaplibreNative.Routing.Core.Graph;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Routing;

/// <summary>
/// Routes over the user's trails and the map's roads together, as one network, for the offline hybrid profiles:
/// the trails are the way, and roads (rural ones first) link them where they don't meet, get round a closure, or
/// reach a start or an end off the trails. A route can go trail, road, trail, road, as the land needs.
///
/// The roads are those of the OpenMapTiles tiles around the trip; the trails are <see cref="RouteOptions.TrackFeatures"/>.
/// A trail's end within 50 m of a road or another trail is joined to it, and a trail crossing a road at grade
/// meets it there (a bridge over it doesn't). <see cref="RouteOptions.Closures"/> are left out of both. The costing
/// makes a metre of trail the cheapest, rural roads twice its time, bigger roads more, motorways closed to it.
/// </summary>
public sealed class HybridGraphRouter : IRoutingEngine
{
    private const double MaxSnapDistanceM = 2000;
    private const double AlternativePenalty = 3;

    public async Task<DirectionsRoute?> RouteAsync(RouteOptions options) =>
        (await FindAsync(options, 1).ConfigureAwait(false)).FirstOrDefault();

    /// <summary>
    /// Up to <paramref name="maxCount"/> routes, shortest first: after each, the stretches it used are made dearer and
    /// the network searched again, keeping a route that's more than 10% different in length.
    /// </summary>
    public async Task<List<DirectionsRoute>> FindAlternativesAsync(RouteOptions options, int maxCount) =>
        await FindAsync(options, Math.Clamp(maxCount, 1, 5)).ConfigureAwait(false);

    private static async Task<List<DirectionsRoute>> FindAsync(RouteOptions options, int count)
    {
        if (string.IsNullOrEmpty(options.MvtTileJsonUrl))
            throw new InvalidOperationException("MvtTileJsonUrl must be set on RouteOptions for an offline hybrid profile.");
        var progress = options.Progress;
        var ct = options.CancellationToken;

        progress?.Report("Resolving tile source…");
        var tiles = await MvtRouter.GetOrCreateProviderAsync(options.MvtTileJsonUrl, options.TileCacheProvider, ct)
            .ConfigureAwait(false);
        var costing = new MvtCostingModel(options.Profile, options.HighwaysFor(options.Profile), options.UseFerry);

        var segments = await MvtGraphBuilder.LoadSegmentsAsync(tiles,
            options.Origin.Lat, options.Origin.Lon, options.Destination.Lat, options.Destination.Lon, ct, progress)
            .ConfigureAwait(false);
        int roads = segments.Count;
        segments = RoadJunctions.ApplyClosures(segments, options.Closures);
        foreach (var trail in RouteClosure.Apply(options.TrackFeatures, options.Closures))
        {
            if (trail.Coordinates.Count < 2) continue;
            segments.Add(new MvtRoadSegment
            {
                Coordinates = trail.Coordinates, RoadClass = MvtCostingModel.TrailClass, Name = trail.Name,
            });
        }
        progress?.Report($"Joining {segments.Count - roads:N0} trail piece(s) with {roads:N0} road piece(s)…");
        var graph = RoadGraph.Build(RoadJunctions.Connect(segments), costing, options.Closures);

        // Both in one connected part: a destination on a trail nothing joins is reached as near as can be
        if (graph.NearestConnected(options.Origin, options.Destination) is not var (start, goal)) return [];
        if (RouteUtils.HaversineMeters(options.Origin.Lat, options.Origin.Lon, start.Lat, start.Lon) > MaxSnapDistanceM ||
            RouteUtils.HaversineMeters(options.Destination.Lat, options.Destination.Lon, goal.Lat, goal.Lon) > MaxSnapDistanceM)
            return [];

        var routes = new List<DirectionsRoute>();
        for (int attempt = 0; attempt < count + 2 && routes.Count < count; attempt++)
        {
            progress?.Report(attempt == 0 ? "Searching for route…" : $"Looking for another route ({routes.Count + 1})…");
            var path = BidirectionalAStarSolver.FindPath(graph, start, goal, costing.MaxSpeedMps, ct);
            if (path is null || path.Count == 0) break;

            var route = ToRoute(path, graph, options.Profile);
            if (routes.All(r => Math.Abs(r.Distance - route.Distance) > 0.1 * Math.Min(r.Distance, route.Distance)))
                routes.Add(route);

            // The next search, away from this one's way
            foreach (var edge in path)
            {
                edge.Cost *= AlternativePenalty;
                foreach (var back in graph.ForwardAdjacency[edge.ToNodeId])
                    if (back.ToNodeId == edge.FromNodeId) back.Cost *= AlternativePenalty;
            }
        }
        routes.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return routes;
    }

    private static DirectionsRoute ToRoute(List<RoadEdge> path, RoadGraph graph, RouteProfile profile)
    {
        var (steps, shape) = MvtManeuverGenerator.Generate(path, graph);
        double distance = path.Sum(e => e.DistanceMeters);
        // The time, not the preference-weighted cost: what the user will spend
        double duration = path.Sum(e => e.TravelSeconds);
        double trail = path.Where(e => e.RoadClass == MvtCostingModel.TrailClass).Sum(e => e.DistanceMeters);
        var leg = new RouteLeg
        {
            Distance = distance,
            Duration = duration,
            Summary = trail >= distance / 2 ? "Mostly trail" : steps.Count > 0 && steps[0].StreetNames.Count > 0 ? steps[0].StreetNames[0] : "Route",
            Steps = steps,
            Shape = shape,
        };
        return RouteUtils.AttachHighwayWarning(new DirectionsRoute { Distance = distance, Duration = duration, Legs = [leg], Profile = profile });
    }
}
