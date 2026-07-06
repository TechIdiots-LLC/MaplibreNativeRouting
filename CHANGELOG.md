# Changelog

## master
### ✨ Features and improvements
- _...Add new stuff here..._

### 🐞 Bug fixes
- _...Add new stuff here..._

## 0.0.8
### 🐞 Bug fixes
- `SpatialGraph.Build` now uses a spatial grid index (O(1) cell lookups) instead of a linear scan (O(N²)) when merging nearby nodes; with statewide trail packages containing hundreds of thousands of coordinates the old code would take minutes — with the grid it completes in under a second

## 0.0.7
### 🐞 Bug fixes
- `NavigationSession.StartAsync` now runs the routing engine on a thread-pool thread via `Task.Run`, keeping the MAUI main thread free to process `IProgress<string>` callbacks in real time; previously the engine ran on the main thread, blocking the UI and preventing progress messages from appearing until routing completed

## 0.0.6
### ✨ Features and improvements
- `IProgress<string>? Progress` on `RouteOptions` — pass a `Progress<string>` callback to receive real-time routing phase messages; callbacks are marshalled back to the capturing `SynchronizationContext` (i.e. the UI thread when created from a MAUI command handler)
- `HybridRouter` reports phases: "Building trail graph…", "Routing road → trail entry…", "Routing on trail…", "Routing trail exit → road…", "Stitching route…", and fallback messages when no trail data or snap is available
- `MvtRouter` reports phases: "Resolving tile source…", tile download count ("Downloading tiles (N/total)…"), "Parsing N tile(s)…", "Building road graph…", "Searching for route…", and retry messages
- `TileProvider.GetCorridorTilesAsync` reports per-tile download progress via `IProgress<string>`
- `NavigationSession.StartAsync` respects caller-provided `TrackFeatures` — if `RouteOptions.TrackFeatures` is already populated the session skips its own `IRouteDataSource` fetch, eliminating a redundant round-trip when the caller has already loaded features

### 🐞 Bug fixes

## 0.0.5
### ✨ Features and improvements
- `ITileCacheProvider` interface — optionally share a tile cache between the routing plugin and the host app's map renderer; routing plugin reads from the shared cache before HTTP and writes back tiles it downloads
- `RouteOptions.TileCacheProvider` — set to your `ITileCacheProvider` implementation to enable shared caching; null (default) preserves existing HTTP + in-memory behavior unchanged
- `ITileCacheProvider.RequestAreaCacheAsync` — routing plugin fires a corridor pre-cache request when planning a route, allowing host apps to delegate to an offline download manager (e.g. `MbglOfflineManager.CreateRegionAsync`)
- `TileCoord` is now public — host app implementations of `ITileCacheProvider` can use the same tile coordinate utilities (`FromLatLon`, `CoverBoundingBox`, etc.)

### 🐞 Bug fixes

## 0.0.4
### ✨ Features and improvements
- Pure C# offline MVT road routing — build a road graph directly from OpenMapTiles vector tiles, eliminating the Valhalla HTTP dependency for auto, bicycle, and pedestrian profiles (`OfflineAuto`, `OfflineBicycle`, `OfflinePedestrian`)
- Minimal protobuf reader and MVT decoder for parsing PBF tiles without external dependencies
- Tile fetching with LRU cache and request coalescing (pattern inspired by maplibre-contour)
- Bidirectional A\* pathfinding on directed road graph with profile-specific costing (speed tables, highway/ferry/toll/surface penalties)
- Turn-by-turn maneuver generation from MVT edge paths with street name merging and bearing-based turn classification
- Hybrid offline routing (`HybridOfflineMotorcycle`, `HybridOfflineBicycle`) — GeoJSON trail-primary routing with MVT road gap-fill via `HybridRouter`
- `HybridRouter` now accepts an injectable `IRoutingEngine` for the road segment, enabling use of `MvtRouter` instead of Valhalla
- `MvtTileJsonUrl` on `RouteOptions` — user-configurable MVT tile source (no hardcoded default)

### 🐞 Bug fixes

## 0.0.3
### ✨ Features and improvements
- Switched NuGet Trusted Publishing from manual OIDC curl flow to `NuGet/login@v1` action for more reliable publishing

### 🐞 Bug fixes
- Fixed NuGet OIDC token exchange failing with HTTP 405 due to manual curl-based flow

## 0.0.2
### ✨ Features and improvements
- Added OIDC Trusted Publishing support for NuGet releases (no more API key secrets)
- Added `id-token: write` permission to release workflow for OIDC token exchange

### 🐞 Bug fixes
- Removed hardcoded local NuGet source (`C:\Users\...\local-nuget`) from `nuget.config` that caused CI build failures

## 0.0.1
### ✨ Features and improvements
- Initial release: `MaplibreNative.Routing.Core` — platform-agnostic routing models, Valhalla router, A\* track-graph router, hybrid router, turn-by-turn navigation tracker, and maneuver announcer
- Initial release: `MaplibreNative.Routing` — MAUI handlers, `NavigationPanel` control, `RouteOverlay`, `NavigationSession`, and `UseMapLibreRouting` builder extension
- Initial release: `MaplibreNative.Routing.Wpf` — WPF handlers, `NavigationPanel` UserControl, GPS-agnostic `NavigationSession`, and `AddMaplibreRouting` DI extension
- Valhalla routing with pedestrian, bicycle, motorcycle, and auto profiles
- Highway avoidance (`use_highways: 0.1`) with `HighwayWarning` annotation and orange dashed overlay
- Hybrid routing: automatically stitches Valhalla road segments with A\* trail segments when both ends snap to imported track features
- `RouteProgressTracker` snaps GPS position to route shape and advances steps at 30 m threshold
- `ManeuverAnnouncerHelper` fires announcement events at 1000 m / 200 m / 30 m thresholds
- `NavigationPanel` dark overlay with dismissible highway warning bar, maneuver icon, instruction text, step distance, and ETA
