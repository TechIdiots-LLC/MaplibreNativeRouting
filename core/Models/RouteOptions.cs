using MaplibreNative.Routing.Core.Mvt;

namespace MaplibreNative.Routing.Core.Models;

/// <summary>Input to any IRoutingEngine. Callers fill in the required fields;
/// optional fields override router defaults.</summary>
public record RouteOptions
{
    public required (double Lat, double Lon) Origin { get; init; }
    public required (double Lat, double Lon) Destination { get; init; }

    /// <summary>Stops on the way, in order: the route has a leg to each, then one to the destination.</summary>
    public IReadOnlyList<(double Lat, double Lon)> Waypoints { get; init; } = [];

    /// <summary>Places no route may pass (a closed trail, a washed-out road), trails and roads alike.</summary>
    public IReadOnlyList<RouteClosure> Closures { get; init; } = [];
    public required RouteProfile Profile { get; init; }

    /// <summary>Valhalla endpoint. Defaults to the public OSM instance.</summary>
    public string ValhallaUrl { get; init; } =
        "https://valhalla1.openstreetmap.de/route";

    /// <summary>TileJSON endpoint or direct tile URL template for MVT tile source.
    /// Required when using Offline or HybridOffline profiles.</summary>
    public string? MvtTileJsonUrl { get; init; }

    /// <summary>Optional shared tile cache provider. When set, TileProvider checks
    /// this cache before HTTP and writes back after download. Pass null (default)
    /// to use HTTP + in-memory caching only.</summary>
    public ITileCacheProvider? TileCacheProvider { get; init; }

    /// <summary>
    /// How willing to take highways, 0 (avoid them as far as possible) to 1 (no different from other roads), offline
    /// and in Valhalla requests alike. Null, the default: the profile's own, highways avoided strongly (0.1) on the
    /// motorcycle and trail profiles and not at all by car.
    /// </summary>
    public double? UseHighways { get; init; }

    /// <summary><see cref="UseHighways"/>, or the profile's own when it isn't set.</summary>
    public double HighwaysFor(RouteProfile profile) => UseHighways ?? profile switch
    {
        RouteProfile.Auto or RouteProfile.OfflineAuto => 1.0,
        _ => 0.1,
    };

    /// <summary>
    /// Kinds of road not to use at all, beyond those the profile leaves out (a car takes no tracks, hybrid moto no
    /// footpaths). Offline, any of them; in a Valhalla request, highways, unpaved roads and ferries (Valhalla has no
    /// way to leave out the others). The user's trails are always used.
    /// </summary>
    public RoadTypes AvoidRoads { get; init; } = RoadTypes.None;

    /// <summary>Avoid ferry routes.</summary>
    public double UseFerry { get; init; } = 0.0;

    /// <summary>Extra Valhalla costing options merged into the request body.
    /// Keys must be valid for the chosen costing model.</summary>
    public Dictionary<string, object> ValhallaExtras { get; init; } = new();

    /// <summary>Track features (LineString coordinate arrays) available for
    /// TrackGraphRouter / HybridRouter. Usually comes from IRouteDataSource.</summary>
    public IReadOnlyList<TrackFeature> TrackFeatures { get; init; } = [];

    public CancellationToken CancellationToken { get; init; }

    /// <summary>Optional progress sink. Each engine reports short status strings such as
    /// "Downloading tiles (3/18)…" or "Searching for route…". Callbacks are invoked on
    /// whatever context the engine runs on — callers should wrap with
    /// <c>new Progress&lt;string&gt;(msg =&gt; ...)</c> to marshal back to the UI thread.</summary>
    public IProgress<string>? Progress { get; init; }
}
