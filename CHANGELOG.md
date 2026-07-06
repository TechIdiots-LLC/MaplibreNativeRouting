# Changelog

## master
### ✨ Features and improvements
- _...Add new stuff here..._

### 🐞 Bug fixes
- _...Add new stuff here..._

## 0.0.12
### 🐞 Bug fixes
- `SpatialGraph.Build` now uses a dynamically computed grid neighbourhood in `GetOrCreateNode` instead of a hardcoded 3×3 search; at 45°N latitude, longitude cells are only ~15.7 m wide, so the previous 3×3 neighbourhood covered just ~27 m — nodes 30–50 m apart (well within `EndpointMergeM=50 m`) were silently missing their merge, leaving many trail segments disconnected
- `SpatialGraph.InsertMissingJunctions` now snaps the source trail's endpoint to the projected T-junction location after inserting it into the target trail; previously the source endpoint and the inserted projection foot were at different coordinates (0–75 m apart), and Phase 2 used `InteriorMergeM=5 m` for the inserted foot — if the source was processed first, the two nodes were never merged regardless of endpoint merge radius (ordering bug); snapping both to the same coordinate gives a guaranteed 0 m merge in Phase 2

## 0.0.11
### 🐞 Bug fixes
- `HybridRouter` now returns the trail route immediately when trail A\* succeeds, regardless of snap distances; previously a pin placed >200 m from the trail (e.g. on a nearby road) would discard a valid trail path and fall through to a hybrid road stitch that always failed because the trail exit node is in the forest with no road access in the MVT graph
- `NavigationSession.StartAsync` now re-throws `OperationCanceledException` instead of swallowing it; previously a routing timeout caused `StartAsync` to return `null` (showing "No route found") rather than propagating the cancellation so `MapViewModel` could show the proper "Route timed out" message
- `RouteOverlay.SetOrAddSource` now uses a `HashSet<string>` to track which layers have been added rather than relying on `SetGeoJsonSource` throwing when the source doesn't exist; on some MapLibre platforms `SetGeoJsonSource` succeeds silently on an unknown source ID, causing `AddLineLayer` to never be called and the route line to be invisible

## 0.0.10
### 🐞 Bug fixes
- `SpatialGraph.Build` increases `JunctionSnapM` from 25 m to 75 m — catches T-junctions at road crossings and slightly offset trail meets (e.g. two club networks with endpoints 30–70 m apart)
- `SpatialGraph.Build` increases `EndpointMergeM` from 15 m to 50 m — bridges endpoint-to-endpoint gaps up to 50 m (typical road crossing width) so trail segments that share an endpoint only by GPS precision or road-crossing separation form a connected graph
- `SpatialGraph` now exposes `EdgeCount` (total undirected edges in the graph) for diagnostics
- `HybridRouter` refactored to build `SpatialGraph` once and run `AStarSolver` once on the pre-built graph, eliminating a redundant graph build and trail A\* run that `TrackGraphRouter` was triggering internally; progress diagnostic now includes edge count
- `HybridRouter` road-to-trail and trail-to-road segments are now skipped when the origin/destination are already within `SnapThresholdM` of the trail — these road calls would target a trail node in the forest where no road exists in the MVT data, causing unnecessary tile downloads and guaranteed-null results
- `HybridRouter` reports "Trail A\* found no path — network may be disconnected between these locations" when A\* fails, giving a clearer status message than a generic timeout

## 0.0.9
### 🐞 Bug fixes
- `SpatialGraph.Build` now performs a T-junction insertion pass before building the graph (ported from the `addMissingIntersectionPoints` pattern in geojson-path-finder): for each trail segment endpoint, the nearest point on every other trail segment's interior is found via perpendicular projection; if within 25 m, a new coordinate is inserted at that location, creating an explicit graph node at T-intersections; this fixes "no route found" on connected-looking trail networks where one trail's endpoint meets the middle of another trail rather than its endpoint
- `SpatialGraph.Build` uses a bounding-box pre-filter to keep the junction-insertion pass efficient (O(F) per endpoint instead of O(F²))
- `HybridRouter` now falls through to hybrid stitch (road bridge + trail A\*) when pure trail-only routing returns null due to a disconnected trail graph, instead of immediately returning "no route found"
- Progress messages now show trail graph node/feature count and snap distances to aid diagnosis

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
