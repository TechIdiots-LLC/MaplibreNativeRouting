# Changelog

## master
### ✨ Features and improvements
- _...Add new stuff here..._
- `NavigationSession.PlanAsync` plans and draws the routes without the GPS, to choose one before setting off, and `FollowAsync` then follows it (`StartAsync` is the two together). An app planning a ride away from its start no longer has it rerouted at once from where the device is, and Start no longer has to plan again
- The offline routers load the tiles along the trip, not the box round it: those within 30% of the trip's length (at least 5 km) of the straight line from origin to destination (`TileCoord.CoverCorridor`). A 136 km diagonal trip (Vermont to New Hampshire) is 5,547 tiles where the box was 9,016. The width is now in metres east-west too; it was in degrees of latitude, narrower than meant away from the equator
- Tile download progress is reported about every 2%, not every tile (a long trip reported 9,000 times)

### 🐞 Bug fixes
- _...Add new stuff here..._
- A simulated drive follows a reroute (MAUI and WPF): it kept driving the old route, so after a closure or leaving the route it left the new one at once, and rerouted again and again
- `NavigationSession` (MAUI) stops the GPS listener only if it started it: ending navigation stopped it under an app still using it, such as one recording a track. A simulated drive ending no longer starts the GPS for a route that was only planned

## 0.1.0
### ✨ Features and improvements
- Hybrid offline routing is trails and roads together: `HybridGraphRouter` routes `HybridOfflineMotorcycle` and `HybridOfflineBicycle` over the user's trails and the map's roads as one network, so a route goes trail, rural road, trail as needed, and reaches the trails by road from a start off them. A trail's end within 50 m of a road or trail is joined to it, and a trail crossing a road at grade meets it. The hybrid profiles have their own costing at last (before, every road, motorways included, cost 30 km/h): trails preferred, minor roads twice their time, bigger ones three and four times, motorways and footpaths closed. A destination on a trail nothing joins is reached as near as the network goes. On real tiles, a route from Holden centre to a trail now starts at the origin and takes roads to it; before, it began 2.1 km away on the trail
- Closures: `RouteOptions.Closures` (a place and a radius) are left out of every route, trails and roads alike, cut exactly at their edge; `AddClosureAsync` on the engine and sessions adds one on the way and reroutes round it at once, keeping it for later reroutes. Offline profiles, `TrackOnly` and Valhalla (as excluded polygons) honour them. On real tiles, a trail closed in its middle is left for 356 m of road and rejoined
- Stops on the way: `RouteOptions.Waypoints`, routed leg by leg with any engine (`MultiStopRouting`) into one route of several legs. `NavigationEngine` raises `WaypointReached` and says "You have reached stop 1" as each is reached, and a reroute keeps only the stops still ahead. `RouteWaypoints` edits the points as a user plans, ported from maplibre-gl-directions: origin and destination at the ends, a stop added where it lengthens the trip least, a point put on the route inserted between the stops of its leg, a point tapped to remove it. MAUI's `RouteOverlay.ShowWaypoints` draws them (A, numbered stops, B), and `MapTapTolerance.Meters` turns a finger's width into metres at the map's zoom for matching taps
- Following camera: `session.Camera` (MAUI) moves the map with navigation, the GPS's or a simulated drive's. Following, the user sits low in the view, the view turns with the road a little ahead of them, tilts 45° to 60° by the distance to the next turn and zooms (12 to 16) to fit them and the turn, refitting only at a new step and 125, 70 and 15 s before the turn: maplibre-navigation-android's DynamicCamera. Panning or zooming pauses it (`IsPaused`, `PausedChanged`) until `Recenter()`; `ShowOverview()` shows the rest of the route; `NavigationCameraMode.NorthUp` keeps north up. `NavigationCamera` (Core) gives the position on any platform. `RouteProgress` gains `LegDistanceTraveledMeters`
- Spoken instructions: `session.Voice.IsEnabled = true` speaks them (MAUI's TextToSpeech; Windows speech on WPF), and `VoiceInstruction` raises each as text. The start, each turn about 60, 15 and 4 seconds ahead (never nearer than 1000, 200 and 30 m; a step begun close to its turn gets only the nearer ones), a new street name once as it's reached, a new route, and the arrival. Worded for speech from each step ("Head SW on Main Street / MA 31" is said "Head southwest on Main Street, Route 31"); distances rounded as maplibre-navigation-ios's DistanceFormatter does, in metric, imperial or UK units (`NavigationOptions.Units`, by default the region's)
- Rerouting: `NavigationEngine` (Core) follows the route from each location fix and, when the user leaves it, plans a new one from where they are with the same options; MAUI and WPF `NavigationSession`s run on it and gain `OffRoute`, `Rerouted`, `RerouteFailed` and `Arrived`. Leaving the route is maplibre-navigation-android's `OffRouteDetector`, with its defaults (`NavigationOptions`): over 50 m from the current step (half that approaching a turn, more with poor GPS accuracy), or on it but getting further from the next turn over three fixes; a turn taken a little early isn't leaving it; 50 m moved and 3 s between reroutes. On a real Holden→Paxton route, driving it with GPS wander raised no reroute and arrived; driving another route left it 55 m out and rerouted from there in 0.4 s
- `RouteProgressTracker` snaps to the nearest point of the route's line rather than its nearest vertex, looking ahead of where it was so a route that doubles back isn't jumped along; `RouteProgress` gains `DistanceFromRouteMeters`, `HasArrived` (within 40 m of the end) and the `Location` fix
- Simulated drives: `RouteSimulator` drives a route, at the speed each step was timed at, or any line as location fixes, in real time (`Start`, with a `SpeedFactor`) or a step at a time (`Next`, `All`). `NavigationSession.StartSimulation(speedFactor, path)` feeds it to navigation in place of the GPS (MAUI draws the position; WPF raises `SimulatedLocationChanged`); a `path` that's another route tries leaving this one. Ported from maplibre-navigation-android's ReplayRouteLocationEngine
- `MaplibreNative.Routing` is built against MapLibreNative.Maui.Handlers 5.0.0-pre.4 (from 4.1.0-dev2) and Microsoft.Maui.Controls 10.0.110, which 5.0 requires. An app on MapLibre MAUI 5 (TrailStumbler) was already resolving 5.0 under a plugin compiled for 4.x; the routing overlay's calls (GeoJSON sources, line layers) are unchanged
- Offline MVT routes name every step: a road piece takes the name and number of the OpenMapTiles `transportation_name` line it runs alongside (within 12 m, the same class first, not one crossing it), rather than only one sharing an end with it, which few did. Road numbers are written as their network has them (`MA 31`, `I 190`, `US 20`) from the tiles' bare `ref`, so a step reads "Holden Road / MA 31". On a real Holden→Paxton route, 12 of its 15 steps are named, where 2 of 42 were
- Offline MVT routes give fewer, truer steps: on the same road, a bend where no other road meets, a slight bend through a junction, or a bend where the road is still the straightest way on is no maneuver; a step of a few metres folds into the one before. The Holden→Paxton route went from 49 steps, most "Bear left/right" along one street, to 15
- `RoadSearch.FindAsync(query, lat, lon, radiusKm)`: a road, or the corner of two, by name near a place, from the same OpenMapTiles tiles the offline router uses: "Elm St", "Route 31" (one road, MA 31, whatever each town calls it), "Main St and Route 31" (where they meet), a misheard "Pheasant St" (Pleasant Street); nearest first, with a `Location` to route to
- `RoadNames`: road names as people say them, shared with AirScribe's road matching. `Words` spells abbreviations out ("Elm St." is "elm street"), `RefNames` gives the ways a number is said ("MA 9" is Route 9, State Route 9, Highway 9), `Base` drops a name's kind and directions ("North Elm Street" is Elm), `Distance` measures a mishearing, `FormatRef` writes an OpenMapTiles ref

### 🐞 Bug fixes
- The online hybrid profiles (`HybridMotorcycle`, `HybridBicycle`) take the road to the trail when the start or end is off it, and the road all the way when the trails don't connect the two: a route could begin kilometres from the origin, at the trail, or end at the first trail's entrance far short of the destination
- The MVT tile source is kept per TileJSON URL (a second source got the first one's tiles), and a `{z}/{x}/{y}` template is accepted in place of a TileJSON
- Roads under construction (`*_construction` classes) aren't routed on
- Offline MVT routes are the cheapest: the bidirectional A* stopped when the two searches' smallest estimates *added* reached the best route met, which counts the distance twice, so it could stop on the first route met and miss a cheaper one (a trail beside a road; a faster road that's a little longer, under a tight estimate such as walking's). It now stops when either side's smallest estimate reaches it, read from the queue's head
- Maneuver announcements name the turn ahead: `ManeuverAnnouncementEventArgs.Step` was the step being driven, whose own maneuver is already behind, at the distance to the next one's. It's now the upcoming step, and the args carry the `Text` to say
- NOTICE.md reproduces each source's copyright lines and licence text in full, as MIT, ISC and BSD require, rather than linking to them; maplibre-navigation-android's copyright lines are corrected (Kurviger, Maplibre, Flitsmeister, Mapbox), and geojson-path-finder (ISC) and Turf (MIT), which `SpatialGraph` follows, are credited
- Offline MVT routing finds routes: roads in vector tiles mostly share no vertex where they meet (simplifying drops the junction's node, a road ending on another stops beside it, and tile edges cut roads mid-segment), so the road graph fell into thousands of pieces and A* found no path. `RoadJunctions` now puts a shared node where two roads on the same level cross and puts an end lying on another road onto it, leaving bridges, tunnels and other `layer`s apart; graph nodes are keyed by rounded coordinates. On real OpenMapTiles tiles a Holden→Paxton graph went from 3,725 pieces (largest 11%) to one holding 96% of the roads
- Offline MVT routing reads gzipped tiles: `TileProvider` asks for and unpacks compressed responses, and `MvtDecoder` unpacks a tile that arrives still gzipped (a cache provider's, or a server that sends PMTiles archives' tiles so regardless). A server sending them gzipped, as PMTiles servers do, gave a graph with no roads
- Tests for Core (`tests/MaplibreNative.Routing.Core.Tests`), run by CI

## 0.0.13
### ✨ Features and improvements
- `IRoutingEngine` gains a default `FindAlternativesAsync(options, maxCount)` method — wraps `RouteAsync` for simple engines, overridable for engines that can generate genuine alternatives
- `HybridRouter.FindAlternativesAsync` computes up to N distinct trail routes by re-running A* with progressively penalised edges from previously found paths (penalty ×3), then sorts results shortest-distance first so the caller can default to `results[0]`
- `AStarSolver.FindPath` accepts an optional `penalizedNodeIds` set and `penalty` multiplier — used by `FindAlternativesAsync` to steer each successive alternative away from previously found corridors
- `NavigationSession.StartAsync` accepts a `maxRoutes` parameter and calls `FindAlternativesAsync`; exposes `RouteAlternatives`, `SelectedRouteIndex`, and a new `SelectAlternative(index)` method for switching between computed alternatives without re-routing
- `RouteOverlay.ShowRoutes` renders the primary (selected) route in blue and all other alternatives in grey below it; alternate routes are stored in a single GeoJSON `FeatureCollection` (`routing-alt-line` layer)

### 🐞 Bug fixes
- `RouteOverlay.SetOrAddSource` now defensively calls `RemoveLayer` / `RemoveSource` before `AddGeoJsonSource` when the layer is not in `_activeLayers`; on some MapLibre platform implementations `RemoveSource` in `ClearRoute` silently succeeds without actually removing the source, causing the subsequent `AddGeoJsonSource` to fail when starting a second route after stopping the first

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
