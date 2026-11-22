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
- **Road search by name** — `RoadSearch` finds a road or a corner near a place from the same tiles: "Elm St", "Route 9", "Main St and Route 9", or a misheard "Pheasant St" for Pleasant Street
- **Rerouting** — notices when you leave the route (too far from it, or heading away from the next turn) and plans a new one from where you are
- **Simulated drives** — drive a route, or any other line, as fake GPS to try navigation and rerouting without going anywhere
- **Turn-by-turn navigation** — `RouteProgressTracker` snaps GPS to route shape and advances steps
- **Stops on the way** — routes through stops, planned leg by leg; `RouteWaypoints` edits them as a user plans (a stop put on the route goes between the stops of its leg), and the overlay draws them; reaching each is announced
- **Following camera** — the map follows you, turned to the road ahead and tilted, zoomed to show the next turn; pausing when you pan, with recenter and an overview of the rest of the route
- **Spoken instructions** — "In 400 feet, turn left onto Main Street, Route 122A", spoken through the platform's text-to-speech when `session.Voice.IsEnabled` is set; announced about 60, 15 and 4 seconds before each turn (never nearer than 1000, 200 and 30 m), in metric, imperial or UK units
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

## Rerouting and simulated drives

`NavigationSession` (MAUI and WPF) runs on `NavigationEngine` in Core, which follows the route from each location fix
and, when you leave it, plans a new route from where you are with the same options:

```csharp
session.OffRoute += (_, e) => status.Text = "Rerouting…";
session.Rerouted += (_, route) => status.Text = $"New route: {route.Distance / 1000:F1} km";   // already drawn
session.RerouteFailed += (_, e) => status.Text = e.Reason;
session.Arrived += (_, _) => status.Text = "You have arrived";
```

You've left the route when you're further than 50 m from the current step (25 m approaching a turn, more when the
GPS reports poor accuracy), or on it but getting further from the next turn over three fixes. A turn taken a little
early isn't leaving it. After a reroute you must move 50 m before leaving the new route counts, and reroutes are at
least 3 s apart. These are maplibre-navigation-android's rules and defaults; `NavigationOptions` changes them, and
`AutoReroute = false` only reports `OffRoute`.

To try it without going anywhere, drive the route as simulated GPS; the MAUI overlay draws the simulated position:

```csharp
session.StartSimulation(speedFactor: 4);                                     // the route, 4× real time
session.StartSimulation(speedFactor: 4, path: session.RouteAlternatives[1]);  // another route: leaves this one, and reroutes
session.StopSimulation();                                                    // back to the device's GPS
```

`RouteSimulator` drives each step at the speed it was timed at. In tests, step it with `Next()` or `All()` and feed
`NavigationEngine.UpdateLocation` directly.

## Spoken instructions

```csharp
session.Voice.IsEnabled = true;                                  // MAUI TextToSpeech; Windows speech on WPF
session.VoiceInstruction += (_, text) => banner.Text = text;      // the same text, to show
```

Each turn is announced three times as it nears: about 60, 15 and 4 seconds ahead at the speed being driven, but
never nearer than 1000, 200 and 30 m ("In 0.5 miles, turn left onto Pleasant Street, Route 122", then "In 0.2
miles…", then "Turn left onto Pleasant Street, Route 122"). Going straight on into a new street name is said once, as
it's reached. The start ("Head southwest on Maple Street"), a new route and the arrival are said too. Distances
are rounded as a driver hears them, in the region's units unless `NavigationOptions.Units` says otherwise.

## Stops on the way

```csharp
var points = new RouteWaypoints();
points.SetOrigin((42.352, -71.863));
points.SetDestination((42.311, -71.928));
points.AddStop((42.33, -71.90));                 // goes where it lengthens the trip least
await session.StartAsync(points.ApplyTo(new RouteOptions { Origin = default, Destination = default, Profile = RouteProfile.OfflineAuto, MvtTileJsonUrl = tiles }));
overlay.ShowWaypoints(points);                   // A, 1, 2… B

// A long-press on the route line puts a stop between the stops of the leg it falls on
double tolerance = MapTapTolerance.Meters(controller, e.LatLng.Latitude);
points.InsertOnRoute((e.LatLng.Latitude, e.LatLng.Longitude), session.ActiveRoute!, tolerance);
```

`RouteOptions.Waypoints` are the stops between the origin and the destination; the route has a leg to each.
`WaypointReached` (and "You have reached stop 1") marks each as it's reached; a reroute keeps only the stops still
ahead. The editing model is maplibre-gl-directions'.

## Following camera

```csharp
// MAUI: once the map's style has loaded
session.Camera.SetController(map.Handler is MapLibreMapHandler h ? h.Controller : null);
session.Camera.SetViewport(map.Width, map.Height);
session.Camera.Camera.BottomInset = sheet.Height;              // panels over the map
session.Camera.PausedChanged += (_, _) => recenterButton.IsVisible = session.Camera.IsPaused;
recenterButton.Clicked += (_, _) => session.Camera.Recenter();
overviewButton.Clicked += (_, _) => session.Camera.ShowOverview();
```

Following, the user sits low in the view so more of the road ahead shows, the view turns with the road a little
ahead of them, tilts 45° to 60° (more the further the next turn is) and zooms to fit them and the next turn, between
zoom 12 and 16, changing only at a new step and as the turn nears: maplibre-navigation-android's DynamicCamera.
`NavigationCameraMode.NorthUp` keeps north up and the map flat. Panning or zooming the map pauses it until
`Recenter()`. On WPF, `NavigationCamera.For(progress)` gives the position for your own map control.

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

## Finding a road by name

`RoadSearch` reads the same OpenMapTiles tiles (`transportation_name` at zoom 14) to find roads by name near a
place, for a destination typed or spoken:

```csharp
var search = new RoadSearch("https://your-tileserver/data/openmaptiles.json");
var found = await search.FindAsync("Main St and Route 31", lat, lon, radiusKm: 10);
// found[0]: "Main Street and MA 31 (Highland Street)", IsCorner, Location = where they meet
await session.StartAsync(new RouteOptions
{
    Origin = (lat, lon), Destination = found[0].Location, Profile = RouteProfile.OfflineAuto,
    MvtTileJsonUrl = "https://your-tileserver/data/openmaptiles.json",
});
```

- **Names however they're written**: "Elm St." is Elm Street; "Highland" alone finds Highland Street (`Level = Base`).
- **Road numbers however they're said**: "Route 31", "Rt 31", "State Route 31" and "MA 31" all find the road numbered
  MA 31, as one road whatever each town calls its stretch; "I-190" and "Interstate 190" find I 190.
- **Corners**: two roads joined by "and", "at" or "&" give the point where they meet first, then each road.
- **Misheard names**: "Pheasant Street" finds Pleasant Street (`Level = Fuzzy`) when no road is called that.
- **Nearest first**: a road is the stretches of its name that join, so Main Street here and in the next town are two
  results; `Location` is the road's point nearest the search's centre.

The rules are in `RoadNames`, public for an app's own matching. They're shared with AirScribe's road matching.

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
