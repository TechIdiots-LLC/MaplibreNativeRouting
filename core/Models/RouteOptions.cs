using MaplibreNative.Routing.Core.Mvt;

namespace MaplibreNative.Routing.Core.Models;

/// <summary>Input to any IRoutingEngine. Callers fill in the required fields;
/// optional fields override router defaults.</summary>
public record RouteOptions
{
    public required (double Lat, double Lon) Origin { get; init; }
    public required (double Lat, double Lon) Destination { get; init; }
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

    /// <summary>Strongly avoid highways (0 = impossible, 1 = neutral).
    /// Applied to all Valhalla costing calls.</summary>
    public double UseHighways { get; init; } = 0.1;

    /// <summary>Avoid ferry routes.</summary>
    public double UseFerry { get; init; } = 0.0;

    /// <summary>Extra Valhalla costing options merged into the request body.
    /// Keys must be valid for the chosen costing model.</summary>
    public Dictionary<string, object> ValhallaExtras { get; init; } = new();

    /// <summary>Track features (LineString coordinate arrays) available for
    /// TrackGraphRouter / HybridRouter. Usually comes from IRouteDataSource.</summary>
    public IReadOnlyList<TrackFeature> TrackFeatures { get; init; } = [];

    public CancellationToken CancellationToken { get; init; }
}
