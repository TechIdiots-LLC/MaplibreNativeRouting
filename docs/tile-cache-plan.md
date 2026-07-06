# Shared Tile Cache — Design Document

## Overview

The routing plugin's MVT router can optionally share a tile cache with the host
application's map renderer.  When a cache provider is supplied, the router checks
the shared cache before fetching tiles over HTTP and writes back any tiles it
downloads so the map renderer (and future route requests) benefit.

The host app can also be asked to **pre-cache entire corridors** for planned
routes, enabling fully offline routing over areas that haven't been viewed on
the map yet.

## Architecture

```
RouteOptions.TileCacheProvider (optional, set by host app)
    ↓
TileProvider (core/Mvt/TileProvider.cs)
    ↓ lookup order:
    1. ITileCacheProvider.GetTileAsync()  ← shared cache (SQLite, etc.)
    2. In-memory LRU cache               ← fast path (500 tiles)
    3. HTTP download                      ← fallback
    ↓ on HTTP success:
    - Write to in-memory LRU
    - Write-through to ITileCacheProvider.SetTileAsync() (fire-and-forget)
    ↓ on corridor fetch:
    - Fire-and-forget ITileCacheProvider.RequestAreaCacheAsync()
```

When `TileCacheProvider` is null (default), all cache provider calls are skipped
and behavior is identical to pure HTTP + in-memory caching.

## Interface

```csharp
// core/Mvt/ITileCacheProvider.cs

public interface ITileCacheProvider
{
    Task<byte[]?> GetTileAsync(TileCoord coord, CancellationToken ct = default);
    Task SetTileAsync(TileCoord coord, byte[] data, CancellationToken ct = default);
    Task RequestAreaCacheAsync(
        double minLat, double minLon,
        double maxLat, double maxLon,
        int zoom,
        CancellationToken ct = default);
}
```

### Method contracts

| Method | Purpose | Threading | Failure mode |
|---|---|---|---|
| `GetTileAsync` | Read decompressed PBF from cache | Must be thread-safe | Return null |
| `SetTileAsync` | Write tile after HTTP download | Fire-and-forget | Silently ignored |
| `RequestAreaCacheAsync` | Ask host to download a corridor | Fire-and-forget | Silently ignored |

Implementations must return **decompressed** bytes.  If the backing store uses
compression (e.g. Deflate in MapLibre's `cache.db`), decompress before returning.

## Design decisions

1. **Interface in `core/`** — platform-agnostic, follows existing `IRoutingEngine`
   pattern.  Host app provides the implementation.

2. **`TileCoord` made public** — the interface accepts `TileCoord` parameters.
   It's a pure `readonly record struct` with math utilities, safe to expose.

3. **Cache provider returns decompressed bytes** — only the provider knows its
   storage format.  Keeps `TileProvider` simple.

4. **Fire-and-forget writes** — `SetTileAsync` and `RequestAreaCacheAsync` are
   not awaited inline during routing.  Routing is never blocked by cache writes.

5. **Defensive wrapping** — all cache calls in `TileProvider` are wrapped in
   try/catch.  A buggy provider degrades to HTTP-only, never breaks routing.

6. **L1/L2 cache pattern** — in-memory LRU is L1 (fast, bounded), shared cache
   is L2 (persistent).  Tiles promoted from L2→L1 on read.

## MapLibre cache.db integration

For apps using [MaplibreNativeMAUI](https://github.com/TechIdiots-LLC/MaplibreNativeMAUI),
tiles are stored in a SQLite database at:

```
{LocalApplicationData}/MapLibreNative.Maui/{processName}/cache.db
```

The `tiles` table is keyed by `(url_template, pixel_ratio, z, x, y)` with data
as BLOBs and an optional `compressed` flag (1 = Deflate).  A host app
implementation reads/writes this table and delegates area caching to
`MbglOfflineManager.CreateRegionAsync`.

See [README.md](../README.md#shared-tile-cache) for a complete implementation
example.

## Files changed

| File | Change |
|---|---|
| `core/Mvt/ITileCacheProvider.cs` | New interface |
| `core/Mvt/TileCoord.cs` | `internal` → `public` |
| `core/Mvt/TileProvider.cs` | Cache provider integration |
| `core/Models/RouteOptions.cs` | `TileCacheProvider` property |
| `core/Routing/MvtRouter.cs` | Pass provider through |
| `README.md` | Documentation + examples |
