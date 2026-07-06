# MaplibreNativeRouting

A .NET MAUI navigation and routing plugin for [MapLibre Native](https://github.com/maplibre/maplibre-native), built on top of [MaplibreNativeMAUI](https://github.com/TechIdiots-LLC/MaplibreNativeMAUI).

## Packages

| Package | Description |
|---|---|
| `MaplibreNative.Routing.Core` | Platform-agnostic routing models, engines, and navigation logic |
| `MaplibreNative.Routing` | MAUI handlers, `NavigationPanel` control, and `UseMapLibreRouting` builder extension |
| `MaplibreNative.Routing.Wpf` | WPF handlers, `NavigationPanel` UserControl, and `AddMaplibreRouting` DI extension |

## Features

- **Valhalla routing** — pedestrian, bicycle, motorcycle, and auto profiles with highway avoidance
- **Offline MVT routing** — pure C# road routing from OpenMapTiles vector tiles, no external server needed (`OfflineAuto`, `OfflineBicycle`, `OfflinePedestrian`)
- **Track-graph routing** — offline A\* pathfinding over imported GeoJSON/GPX track layers
- **Hybrid routing** — automatically stitches road segments with trail A\* segments (Valhalla or MVT road engine)
- **Shared tile cache** — optional `ITileCacheProvider` lets the routing plugin share tiles with the host app's map renderer cache
- **Turn-by-turn navigation** — `RouteProgressTracker` snaps GPS to route shape and advances steps
- **Maneuver announcements** — fires events at 1000 m / 200 m / 30 m thresholds
- **Highway warnings** — detects motorway/trunk segments and surfaces a dismissible warning bar
- **`NavigationPanel` control** — dark overlay with maneuver icon, instruction, step distance, and ETA

## Requirements

- .NET 10 MAUI (net10.0-android36.0, net10.0-windows10.0.19041.0) **or** .NET 10 WPF (net10.0-windows10.0.19041.0)
- [MaplibreNative.Maui.Handlers](https://github.com/TechIdiots-LLC/MaplibreNativeMAUI) ≥ 4.1.0
- Valhalla HTTP API endpoint (for Valhalla-based profiles) **or** an OpenMapTiles-compatible MVT tile source (for offline profiles)

## Quick start — MAUI

```csharp
// MauiProgram.cs
builder.UseMapLibreRouting(routeDataSource: myLayerRepository);

// In your page/viewmodel
var session = serviceProvider.GetRequiredService<NavigationSession>();
await session.StartAsync(new RouteOptions(origin, destination, RouteProfile.HybridMotorcycle)
{
    ValhallaUrl = "https://your-valhalla-host/route",
    TrackFeatures = await routeDataSource.GetRoutableTrackFeaturesAsync()
});
session.ProgressUpdated += (_, progress) => navigationPanel.Apply(progress);
```

## Quick start — WPF

```csharp
// App startup (e.g. App.xaml.cs / host builder)
services.AddMaplibreRouting(routeDataSource: myLayerRepository);

// In your window/viewmodel
var session = serviceProvider.GetRequiredService<NavigationSession>();
session.ProgressUpdated += (_, progress) => navigationPanel.Apply(progress);

await session.StartAsync(new RouteOptions(origin, destination, RouteProfile.HybridMotorcycle)
{
    ValhallaUrl = "https://your-valhalla-host/route",
});

// From your GPS source (Windows Location API, NMEA parser, VistumblerCS, etc.)
void OnLocationChanged(double lat, double lon) => session.UpdateLocation(lat, lon);
```

## Offline MVT routing

The offline profiles (`OfflineAuto`, `OfflineBicycle`, `OfflinePedestrian`) build a road graph from OpenMapTiles-compatible MVT tiles — no Valhalla server required. Set `MvtTileJsonUrl` to your TileJSON endpoint:

```csharp
await session.StartAsync(new RouteOptions
{
    Origin = (47.6062, -122.3321),
    Destination = (47.6205, -122.3493),
    Profile = RouteProfile.OfflineBicycle,
    MvtTileJsonUrl = "https://your-tileserver/data/openmaptiles.json",
});
```

Hybrid offline profiles (`HybridOfflineMotorcycle`, `HybridOfflineBicycle`) route primarily on GeoJSON trail features and use MVT roads to fill gaps between trail segments.

## Shared tile cache

By default the routing plugin fetches MVT tiles over HTTP and caches them in memory (lost on restart). If your app already caches tiles — for example via [MaplibreNativeMAUI](https://github.com/TechIdiots-LLC/MaplibreNativeMAUI)'s offline manager — you can share that cache with the routing plugin by implementing `ITileCacheProvider`:

```csharp
public interface ITileCacheProvider
{
    Task<byte[]?> GetTileAsync(TileCoord coord, CancellationToken ct = default);
    Task SetTileAsync(TileCoord coord, byte[] data, CancellationToken ct = default);
    Task RequestAreaCacheAsync(
        double minLat, double minLon, double maxLat, double maxLon,
        int zoom, CancellationToken ct = default);
}
```

- **`GetTileAsync`** — return decompressed PBF bytes from your cache, or `null` on miss
- **`SetTileAsync`** — store a tile the routing plugin downloaded (so the map renderer benefits too)
- **`RequestAreaCacheAsync`** — the routing plugin asks your app to pre-cache a corridor; map this to your offline download manager (e.g. `MbglOfflineManager.CreateRegionAsync`)

### Example: MapLibre SQLite cache integration

```csharp
public class MapLibreTileCacheProvider : ITileCacheProvider
{
    private readonly string _dbPath;

    public MapLibreTileCacheProvider(string cacheDbPath)
    {
        _dbPath = cacheDbPath;
    }

    public async Task<byte[]?> GetTileAsync(TileCoord coord, CancellationToken ct)
    {
        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT data, compressed FROM tiles
            WHERE url_template = $url AND z = $z AND x = $x AND y = $y AND pixel_ratio = 1";
        cmd.Parameters.AddWithValue("$url", _urlTemplate);
        cmd.Parameters.AddWithValue("$z", coord.Z);
        cmd.Parameters.AddWithValue("$x", coord.X);
        cmd.Parameters.AddWithValue("$y", coord.Y);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var data = (byte[])reader["data"];
        var compressed = (long)reader["compressed"] == 1;

        if (compressed)
        {
            using var input = new MemoryStream(data);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            await deflate.CopyToAsync(output, ct);
            return output.ToArray();
        }

        return data;
    }

    public async Task SetTileAsync(TileCoord coord, byte[] data, CancellationToken ct)
    {
        // Write to the same SQLite cache so the map renderer can use it
        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(ct);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT OR IGNORE INTO tiles
            (url_template, pixel_ratio, z, x, y, data, compressed)
            VALUES ($url, 1, $z, $x, $y, $data, 0)";
        cmd.Parameters.AddWithValue("$url", _urlTemplate);
        cmd.Parameters.AddWithValue("$z", coord.Z);
        cmd.Parameters.AddWithValue("$x", coord.X);
        cmd.Parameters.AddWithValue("$y", coord.Y);
        cmd.Parameters.AddWithValue("$data", data);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public Task RequestAreaCacheAsync(
        double minLat, double minLon, double maxLat, double maxLon,
        int zoom, CancellationToken ct)
    {
        // Delegate to MapLibre's offline manager to download the region
        return MbglOfflineManager.CreateRegionAsync(
            styleUrl: "https://your-tileserver/styles/your-style/style.json",
            latSw: minLat, lonSw: minLon,
            latNe: maxLat, lonNe: maxLon,
            minZoom: zoom, maxZoom: zoom);
    }
}
```

### Wiring it up

Pass the provider on `RouteOptions`:

```csharp
var cacheProvider = new MapLibreTileCacheProvider(MbglCache.DefaultPath);

await session.StartAsync(new RouteOptions
{
    Origin = (47.6062, -122.3321),
    Destination = (47.6205, -122.3493),
    Profile = RouteProfile.OfflineAuto,
    MvtTileJsonUrl = "https://your-tileserver/data/openmaptiles.json",
    TileCacheProvider = cacheProvider,
});
```

When `TileCacheProvider` is `null` (the default), the plugin uses HTTP + in-memory caching only — no behavioral change from before.

## Attribution

Routing model and navigation patterns adapted from [maplibre-navigation-android](https://github.com/mapbox/maplibre-navigation-android) (MIT License). See [NOTICE.md](NOTICE.md).

## License

MIT — see [LICENSE](LICENSE).
