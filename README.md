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
- **Track-graph routing** — offline A\* pathfinding over imported GeoJSON/GPX track layers
- **Hybrid routing** — automatically stitches Valhalla road segments with trail A\* segments
- **Turn-by-turn navigation** — `RouteProgressTracker` snaps GPS to route shape and advances steps
- **Maneuver announcements** — fires events at 1000 m / 200 m / 30 m thresholds
- **Highway warnings** — detects motorway/trunk segments and surfaces a dismissible warning bar
- **`NavigationPanel` control** — dark overlay with maneuver icon, instruction, step distance, and ETA

## Requirements

- .NET 10 MAUI (net10.0-android36.0, net10.0-windows10.0.19041.0) **or** .NET 10 WPF (net10.0-windows10.0.19041.0)
- [MaplibreNative.Maui.Handlers](https://github.com/TechIdiots-LLC/MaplibreNativeMAUI) ≥ 4.1.0
- Valhalla HTTP API endpoint (for road-profile routing)

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

## Attribution

Routing model and navigation patterns adapted from [maplibre-navigation-android](https://github.com/mapbox/maplibre-navigation-android) (MIT License). See [NOTICE.md](NOTICE.md).

## License

MIT — see [LICENSE](LICENSE).
