namespace MaplibreNative.Routing.Core.Mvt;

/// <summary>
/// Optional shared tile cache between the host app and the routing plugin.
/// When supplied on <see cref="Models.RouteOptions.TileCacheProvider"/>,
/// <see cref="TileProvider"/> checks this cache before HTTP and writes back
/// after download. Implementations must return decompressed PBF bytes.
/// </summary>
public interface ITileCacheProvider
{
    /// <summary>
    /// Reads a tile from the shared cache.
    /// Returns decompressed PBF bytes, or null on cache miss.
    /// Must be thread-safe.
    /// </summary>
    Task<byte[]?> GetTileAsync(TileCoord coord, CancellationToken ct = default);

    /// <summary>
    /// Writes a tile to the shared cache. Called after HTTP download so the
    /// host app's cache benefits too. Should be idempotent.
    /// </summary>
    Task SetTileAsync(TileCoord coord, byte[] data, CancellationToken ct = default);

    /// <summary>
    /// Requests the host app to pre-cache all tiles in the given bounding box
    /// at the specified zoom level. For maplibre-maui-ac users this maps to
    /// <c>MbglOfflineManager.CreateRegionAsync</c>.
    ///
    /// The routing plugin calls this fire-and-forget during corridor fetching.
    /// Host apps may await it for progress tracking. Implementations that do
    /// not support area caching should return <see cref="Task.CompletedTask"/>.
    /// </summary>
    Task RequestAreaCacheAsync(
        double minLat, double minLon,
        double maxLat, double maxLon,
        int zoom,
        CancellationToken ct = default);
}
