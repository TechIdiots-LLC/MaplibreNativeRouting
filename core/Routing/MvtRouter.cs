using MaplibreNative.Routing.Core.Graph;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Mvt;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Routing;

public sealed class MvtRouter : IRoutingEngine
{
    private const double MaxSnapDistanceM = 1000;
    private const int MaxRetries = 2;

    private static TileProvider? _tileProvider;
    private static ITileCacheProvider? _currentCacheProvider;
    private static string? _currentTileJsonUrl;
    private static readonly SemaphoreSlim InitLock = new(1, 1);

    public async Task<DirectionsRoute?> RouteAsync(RouteOptions options)
    {
        if (string.IsNullOrEmpty(options.MvtTileJsonUrl))
            throw new InvalidOperationException(
                "MvtTileJsonUrl must be set on RouteOptions when using an offline or hybrid-offline profile.");

        var progress = options.Progress;

        progress?.Report("Resolving tile source…");
        var provider = await GetOrCreateProviderAsync(
                options.MvtTileJsonUrl, options.TileCacheProvider, options.CancellationToken)
            .ConfigureAwait(false);

        var costing = new MvtCostingModel(options.Profile, options.HighwaysFor(options.Profile), options.UseFerry);

        List<RoadEdge>? path = null;
        RoadGraph? graph = null;

        for (int attempt = 0; attempt <= MaxRetries && path == null; attempt++)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            if (attempt > 0) progress?.Report($"Retrying (attempt {attempt + 1})…");

            graph = await MvtGraphBuilder.BuildAsync(
                provider,
                options.Origin.Lat, options.Origin.Lon,
                options.Destination.Lat, options.Destination.Lon,
                costing, options.CancellationToken, progress, options.Closures).ConfigureAwait(false);

            var startNode = graph.NearestNode(options.Origin.Lat, options.Origin.Lon);
            var goalNode = graph.NearestNode(options.Destination.Lat, options.Destination.Lon);

            if (startNode == null || goalNode == null) continue;

            double startDist = RouteUtils.HaversineMeters(
                options.Origin.Lat, options.Origin.Lon, startNode.Lat, startNode.Lon);
            double goalDist = RouteUtils.HaversineMeters(
                options.Destination.Lat, options.Destination.Lon, goalNode.Lat, goalNode.Lon);

            if (startDist > MaxSnapDistanceM || goalDist > MaxSnapDistanceM) continue;

            progress?.Report("Searching for route…");
            path = BidirectionalAStarSolver.FindPath(
                graph, startNode, goalNode, costing.MaxSpeedMps, options.CancellationToken);
        }

        if (path == null || path.Count == 0 || graph == null)
            return null;

        var (steps, shape) = MvtManeuverGenerator.Generate(path, graph);

        double totalDistance = 0;
        double totalDuration = 0;
        foreach (var edge in path)
        {
            totalDistance += edge.DistanceMeters;
            totalDuration += edge.TravelSeconds;
        }

        string summary = steps.Count > 0 && steps[0].StreetNames.Count > 0
            ? steps[0].StreetNames[0]
            : "Route";

        var leg = new RouteLeg
        {
            Distance = totalDistance,
            Duration = totalDuration,
            Summary = summary,
            Steps = steps,
            Shape = shape,
        };

        var route = new DirectionsRoute
        {
            Distance = totalDistance,
            Duration = totalDuration,
            Legs = [leg],
            Profile = options.Profile,
        };

        return RouteUtils.AttachHighwayWarning(route);
    }

    /// <summary>
    /// The tile source for a TileJSON (or a tile URL template with {z}/{x}/{y}), kept while it's the one asked for:
    /// another source or cache makes a new one.
    /// </summary>
    internal static async Task<TileProvider> GetOrCreateProviderAsync(
        string tileJsonUrl, ITileCacheProvider? cacheProvider, CancellationToken ct)
    {
        bool Same() => _tileProvider != null && ReferenceEquals(_currentCacheProvider, cacheProvider)
                       && _currentTileJsonUrl == tileJsonUrl;
        if (Same()) return _tileProvider!;

        await InitLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Same()) return _tileProvider!;

            string urlTemplate = tileJsonUrl.Contains("{z}")
                ? tileJsonUrl
                : await TileProvider.ResolveTileJsonAsync(tileJsonUrl, ct).ConfigureAwait(false);
            _tileProvider = new TileProvider(urlTemplate, cacheProvider: cacheProvider);
            _currentCacheProvider = cacheProvider;
            _currentTileJsonUrl = tileJsonUrl;
            return _tileProvider;
        }
        finally
        {
            InitLock.Release();
        }
    }
}
