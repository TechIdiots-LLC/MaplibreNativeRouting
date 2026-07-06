# Pure C# Offline MVT Road Routing — Implementation Plan

## Context

MaplibreNativeRouting currently requires a Valhalla HTTP API endpoint for road routing (Auto, Bicycle, Pedestrian profiles). The goal is to add **pure C# offline routing** that builds a road graph directly from OpenMapTiles MVT (Mapbox Vector Tile) data — the same tiles the map renderer already fetches. This eliminates the external Valhalla dependency for users who want self-contained routing.

The existing codebase provides ~40% of what's needed: `SpatialGraph` + `AStarSolver` for pathfinding, `IRoutingEngine` for pluggability, and all the output models (`DirectionsRoute`, `RouteLeg`, `LegStep`) that the navigation UI consumes.

Tile source: `https://tiles.wifidb.net/data/openmaptiles.json` (TileJSON → PBF tiles with `transportation` and `transportation_name` layers).

---

## Architecture Overview

```
RouteOptions (origin, dest, OfflineAuto profile, MvtTileJsonUrl)
    ↓
MvtRouter : IRoutingEngine
    ↓
TileProvider → fetches z14 PBF tiles for routing corridor
    ↓
MvtDecoder → parses PBF → MvtRoadSegments (with coords, road class, oneway, name)
    ↓
RoadGraph.Build() → directed graph with spatial-hash node lookup, tile-seam merging
    ↓
BidirectionalAStarSolver → finds optimal edge path using MvtCostingModel
    ↓
MvtManeuverGenerator → classifies turns, generates instructions
    ↓
DirectionsRoute → feeds existing NavigationPanel, RouteOverlay, highway warnings
```

---

## Phase 1: MVT Parsing Foundation

**Zero dependencies on existing code — can be developed and tested independently.**

### `core/Mvt/ProtobufReader.cs` (~150 lines)
Minimal protobuf wire-format reader operating on `ReadOnlySpan<byte>`.
- `ReadVarint32/64()`, `ReadSInt32()` (zigzag decode), `ReadLengthDelimited()`, `ReadString()`, `ReadFloat/Double()`
- `ReadFieldTag(out int wireType)`, `Skip(wireType)`, `bool HasMore`
- MVT uses only wire types 0 (varint), 2 (length-delimited), and 5 (32-bit fixed)

### `core/Mvt/MvtDecoder.cs` (~200 lines)
Decodes PBF bytes into typed layer/feature objects per MVT Spec v2.1.
- Internal types: `MvtTile`, `MvtLayer` (Name, Extent, Keys, Values, Features), `MvtFeature` (Id, GeomType, Tags, Geometry)
- Geometry decoding: command integers `(id & 0x7) | (count << 3)`, MoveTo/LineTo/ClosePath with delta-encoded zigzag (dx, dy) pairs
- `GetProperties(feature, layer)` → zips tag pairs against keys/values arrays
- Public API: `static MvtTile Decode(ReadOnlySpan<byte> pbfData)`

### `core/Mvt/TileCoord.cs` (~80 lines)
Slippy map tile coordinate math — `readonly record struct TileCoord(int Z, int X, int Y)`.
- `FromLatLon(lat, lon, zoom)` → tile coords
- `GetBounds()` → (North, South, West, East) geographic bbox
- `FeatureToLatLon(fx, fy, extent)` → WGS-84 (handles Mercator projection correctly for latitude)
- `CoverBoundingBox(minLat, minLon, maxLat, maxLon, zoom)` → all intersecting tiles

---

## Phase 2: Tile Fetching & Caching

### `core/Mvt/TileProvider.cs` (~180 lines)
HTTP tile fetcher with LRU cache. Borrows patterns from maplibre-contour's `AsyncCache`:
- **Request coalescing**: concurrent requests for the same tile share a single in-flight `Task<byte[]>` (via `ConcurrentDictionary<TileCoord, Task<byte[]>>`)
- **LRU eviction**: `ConcurrentDictionary<TileCoord, byte[]>` with access-order tracking, configurable capacity (default 500 tiles)
- **TileJSON resolution**: `static async Task<string> ResolveTileJsonAsync(string tileJsonUrl)` — fetches TileJSON, parses `tiles` array via `System.Text.Json.JsonDocument`, returns first URL template
- **Corridor fetching**: `GetCorridorTilesAsync(origin, dest, corridorWidthKm, zoom, ct)` — computes expanded bbox, enumerates tiles, fetches in parallel with `SemaphoreSlim(6)` concurrency limit
- Corridor expansion: `max(straightLineDistance * 0.3, 5.0)` km on each side
- `IDisposable` for `HttpClient` lifecycle

### `core/Models/MvtRoadSegment.cs` (~30 lines)
Internal intermediate representation between raw MVT features and graph edges:
- `Coordinates: IReadOnlyList<(double Lon, double Lat)>`
- `RoadClass, Subclass?, Surface?, Brunnel?, Name?, Ref?` (strings from MVT properties)
- `Oneway` (int: 0=both, 1=forward, -1=backward), `Toll`, `Ramp`, `AccessRestricted` (bools)

---

## Phase 3: Road Graph Data Structures

### `core/Graph/RoadEdge.cs` (~40 lines)
**New class, separate from `GraphEdge`** — directed edge with road attributes.
- `FromNodeId, ToNodeId, DistanceMeters, Cost` (cost set by costing model, in seconds)
- Road attrs: `RoadClass, Subclass?, Surface?, StreetName?, Ref?, IsToll, IsRamp, IsBridge, IsTunnel`
- `IntermediatePoints: IReadOnlyList<(double Lon, double Lat)>` — shape between endpoints
- `SpeedKmh` — from costing model speed table
- Not a record (mutable `Cost` property set during graph construction)

### `core/Graph/RoadGraph.cs` (~200 lines)
Directed graph with spatial hash, reuses existing `GraphNode`.
- **Dual adjacency**: `ForwardAdjacency` and `BackwardAdjacency` (`Dictionary<int, List<RoadEdge>>`)
- **Tile-seam-safe node merging**: round coords to 7 decimal places (~1.1 cm), use `Dictionary<(long latKey, long lonKey), int>` where key = `(long)(coord * 10_000_000)`. Same geographic point from different tiles → same key → merged automatically.
- **Spatial hash for NearestNode**: ~100m grid cells, check cell + neighbors — O(1) amortized vs current O(n) linear scan
- `static RoadGraph Build(IReadOnlyList<MvtRoadSegment> segments, MvtCostingModel costing)`:
  1. For each segment, convert coordinates to nodes via rounding dictionary
  2. Create `RoadEdge` per consecutive coordinate pair with road attributes
  3. Respect `Oneway` (0=bidirectional, 1=forward only, -1=reverse only)
  4. Skip segments where `costing.IsTraversable()` returns false
  5. Set `edge.Cost` via `costing.ComputeCost()`

### `core/Graph/BidirectionalAStarSolver.cs` (~200 lines)
**Separate from existing `AStarSolver`** — operates on `RoadGraph` with `RoadEdge`.
- Forward + backward `PriorityQueue<int, double>` and `Dictionary<int, double>` for gScores
- Alternates expansions (expand whichever has lower minimum f-score)
- Meeting point tracking: when node N reached from both directions, check if `gF[N] + gB[N] < bestCost`
- Terminates when both queues' minimums exceed `bestTotalCost`
- Heuristic: `HaversineMeters / maxSpeedMps` (admissible since cost is in seconds)
- Stores edges in `cameFrom` dicts (not just node IDs) for direct edge-sequence reconstruction
- Returns `List<RoadEdge>?` — ordered edge path, or null
- Accepts `CancellationToken`

---

## Phase 4: Costing Model & Graph Builder

### `core/Routing/MvtCostingModel.cs` (~120 lines)
Profile-specific traversability, speeds, and cost penalties.

**Speed tables** (km/h):

| Road class | Auto | Bicycle | Pedestrian |
|---|---|---|---|
| motorway | 110 | — | — |
| trunk | 90 | — | — |
| primary | 70 | 20 | 5 |
| secondary | 60 | 22 | 5 |
| tertiary | 50 | 24 | 5 |
| minor | 40 | 20 | 5 |
| service | 25 | 15 | 5 |
| track | 30 | 12 | 4 |
| path/cycleway | — | 18 | 4 |
| path/footway | — | 8 | 5 |
| path/steps | — | — | 3 |

"—" = `IsTraversable()` returns false.

**Cost formula**: `baseSeconds = distance / (speed / 3.6)`, then multiply by:
- Highway penalty: `1 + (1 - useHighways) * 9` (default useHighways=0.1 → 9.1x multiplier)
- Ferry penalty: `1 + (1 - useFerry) * 9`
- Toll: 1.5x (auto), 2.0x (bicycle)
- Unpaved surface: 1.3x (auto), 1.5x (bicycle)

### `core/Routing/MvtGraphBuilder.cs` (~120 lines)
Orchestrates tiles → segments → `RoadGraph`.
1. Fetch corridor tiles via `TileProvider.GetCorridorTilesAsync` at z14
2. Decode each tile with `MvtDecoder.Decode()`
3. Extract `transportation` layer LineString features → convert coords via `TileCoord.FeatureToLatLon`
4. Extract `transportation_name` layer → match names to road features by first/last coordinate proximity within same tile
5. Create `MvtRoadSegment` objects
6. Call `RoadGraph.Build(segments, costing)`

---

## Phase 5: Turn Instructions

### `core/Routing/MvtManeuverGenerator.cs` (~300 lines)
Generates `LegStep` list and shape from edge path.

**Algorithm**:
1. Build full shape by concatenating edge geometries (FromNode → IntermediatePoints → [last edge] ToNode)
2. Compute bearings at each edge transition using `RouteUtils.InitialBearing()`
3. Classify turns from normalized angle [-180, 180]:
   - `[-10, 10]` → Continue, `(10, 45]` → SlightRight, `(45, 120]` → Right, `(120, 170]` → SharpRight, `(170, 180]` → UturnRight (mirror for left)
4. Merge consecutive edges with same street name + Continue maneuver into single step
5. Generate instruction text: `"Turn right onto {name}"`, `"Head north on {name}"`, `"Arrive at destination"`
6. Set `RoadClass` on each step (ensures `RouteUtils.AttachHighwayWarning` works unchanged)

### Modified: `core/Utils/RouteUtils.cs`
Add two methods:
- `InitialBearing(lat1, lon1, lat2, lon2)` → degrees [0, 360)
- `TurnAngle(prevBearing, nextBearing)` → degrees [-180, 180], positive = right

---

## Phase 6: Integration

### `core/Routing/MvtRouter.cs` (~100 lines)
New `IRoutingEngine` implementation tying everything together.
1. Lazily resolve TileJSON URL → `TileProvider` (thread-safe via `SemaphoreSlim`)
2. Create `MvtCostingModel` from profile + options
3. Build `RoadGraph` via `MvtGraphBuilder`
4. Snap origin/destination to nearest nodes (fail if > 1000m)
5. Run `BidirectionalAStarSolver.FindPath()` — retry with expanded corridor on failure
6. Generate maneuvers via `MvtManeuverGenerator`
7. Assemble `DirectionsRoute` with single `RouteLeg`
8. Call `RouteUtils.AttachHighwayWarning()` (works as-is)

### Modified: `core/Models/RouteProfile.cs`
Add three enum values:
```csharp
OfflineAuto,
OfflineBicycle,
OfflinePedestrian,
```

### Modified: `core/Models/RouteOptions.cs`
Add property:
```csharp
public string MvtTileJsonUrl { get; init; } = "https://tiles.wifidb.net/data/openmaptiles.json";
```

### Modified: `handlers/NavigationSession.cs` + `wpf/NavigationSession.cs`
Extend engine selection switch:
```csharp
RouteProfile.OfflineAuto or RouteProfile.OfflineBicycle or RouteProfile.OfflinePedestrian
    => new MvtRouter(),
```

---

## File Summary

| File | Status | Lines (est.) |
|---|---|---|
| `core/Mvt/ProtobufReader.cs` | New | ~150 |
| `core/Mvt/MvtDecoder.cs` | New | ~200 |
| `core/Mvt/TileCoord.cs` | New | ~80 |
| `core/Mvt/TileProvider.cs` | New | ~180 |
| `core/Models/MvtRoadSegment.cs` | New | ~30 |
| `core/Graph/RoadEdge.cs` | New | ~40 |
| `core/Graph/RoadGraph.cs` | New | ~200 |
| `core/Graph/BidirectionalAStarSolver.cs` | New | ~200 |
| `core/Routing/MvtCostingModel.cs` | New | ~120 |
| `core/Routing/MvtGraphBuilder.cs` | New | ~120 |
| `core/Routing/MvtManeuverGenerator.cs` | New | ~300 |
| `core/Routing/MvtRouter.cs` | New | ~100 |
| `core/Utils/RouteUtils.cs` | Modified | +20 |
| `core/Models/RouteProfile.cs` | Modified | +3 |
| `core/Models/RouteOptions.cs` | Modified | +1 |
| `handlers/NavigationSession.cs` | Modified | +2 |
| `wpf/NavigationSession.cs` | Modified | +2 |
| **Total** | | **~1,750** |

---

## Key Design Decisions

1. **New `RoadEdge`/`RoadGraph` classes** (not extending `GraphEdge`/`SpatialGraph`) — avoids adding nullable road fields that are meaningless to `TrackGraphRouter`, keeps existing A* behavior untouched
2. **Coordinate-rounding node merge** (7 decimal places = ~1.1 cm) — automatically handles tile seams without distance-based comparisons; same geographic point from any tile rounds to same key
3. **In-memory LRU cache** with request coalescing (pattern from maplibre-contour's `AsyncCache`) — disk caching belongs in host app, not zero-dependency core library
4. **Separate `OfflineAuto/Bicycle/Pedestrian` profiles** — users can choose between Valhalla and offline routing; both remain available
5. **z14 tiles** — full road detail including residential/service; lower zooms drop minor roads

## Risks

- **Tile seam correctness**: mitigated by coordinate-rounding merge + diagnostic counting
- **Memory for large corridors**: LRU cache caps at 500 tiles (~50-100 MB); graph built incrementally
- **No-path on narrow corridor**: retry with 50% expansion (up to 2 retries)
- **PBF parsing edge cases**: test against known tiles, compare feature counts with external tools

## Verification

1. **Unit test PBF parsing**: download a known z14 tile, verify layer names, feature counts, coordinate conversion
2. **Unit test bidirectional A***: hand-craft a small graph, verify optimal path matches expected
3. **Unit test turn classification**: known edge angles → expected ManeuverType values
4. **Integration test**: route a known origin→destination (e.g., within a city), verify:
   - Shape is plausible on the map (overlay GeoJSON in the app)
   - Turn instructions make sense (street names, turn directions)
   - Highway warning fires when route includes motorway/trunk segments
   - `NavigationPanel` renders correctly with the returned `DirectionsRoute`
5. **Tile seam test**: route that crosses tile boundaries, verify continuous path (no gaps)
