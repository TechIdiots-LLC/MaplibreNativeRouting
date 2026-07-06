using System.Collections.Concurrent;
using System.Text.Json;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Mvt;

internal sealed class TileProvider : IDisposable
{
    private const int DefaultMaxCacheSize = 500;
    private const int MaxConcurrentDownloads = 6;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly string _urlTemplate;
    private readonly int _maxCacheSize;

    private readonly ConcurrentDictionary<TileCoord, byte[]> _cache = new();
    private readonly ConcurrentDictionary<TileCoord, Task<byte[]?>> _inflight = new();
    private readonly ConcurrentQueue<TileCoord> _accessOrder = new();

    public TileProvider(string urlTemplate, int maxCacheSize = DefaultMaxCacheSize)
    {
        _urlTemplate = urlTemplate;
        _maxCacheSize = maxCacheSize;
    }

    public static async Task<string> ResolveTileJsonAsync(string tileJsonUrl, CancellationToken ct = default)
    {
        var response = await Http.GetAsync(tileJsonUrl, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var tiles = doc.RootElement.GetProperty("tiles");
        return tiles[0].GetString()
            ?? throw new InvalidOperationException("TileJSON has no tile URL");
    }

    public async Task<byte[]?> GetTileDataAsync(TileCoord coord, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(coord, out var cached))
        {
            _accessOrder.Enqueue(coord);
            return cached;
        }

        var task = _inflight.GetOrAdd(coord, c => DownloadTileAsync(c, ct));
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            _inflight.TryRemove(coord, out _);
        }
    }

    public async Task<List<(TileCoord Coord, byte[] Data)>> GetCorridorTilesAsync(
        double originLat, double originLon,
        double destLat, double destLon,
        int zoom, CancellationToken ct = default)
    {
        double straightLineDist = RouteUtils.HaversineMeters(originLat, originLon, destLat, destLon);
        double expansionKm = Math.Max(straightLineDist / 1000.0 * 0.3, 5.0);
        double expansionDeg = expansionKm / 111.0;

        double minLat = Math.Min(originLat, destLat) - expansionDeg;
        double maxLat = Math.Max(originLat, destLat) + expansionDeg;
        double minLon = Math.Min(originLon, destLon) - expansionDeg;
        double maxLon = Math.Max(originLon, destLon) + expansionDeg;

        var tileCoords = TileCoord.CoverBoundingBox(minLat, minLon, maxLat, maxLon, zoom);

        var semaphore = new SemaphoreSlim(MaxConcurrentDownloads);
        var tasks = tileCoords.Select(async coord =>
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var data = await GetTileDataAsync(coord, ct).ConfigureAwait(false);
                return (Coord: coord, Data: data);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.Where(r => r.Data != null).Select(r => (r.Coord, r.Data!)).ToList();
    }

    private async Task<byte[]?> DownloadTileAsync(TileCoord coord, CancellationToken ct)
    {
        var url = _urlTemplate
            .Replace("{z}", coord.Z.ToString())
            .Replace("{x}", coord.X.ToString())
            .Replace("{y}", coord.Y.ToString());

        try
        {
            var data = await Http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            _cache[coord] = data;
            _accessOrder.Enqueue(coord);
            Prune();
            return data;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private void Prune()
    {
        while (_cache.Count > _maxCacheSize && _accessOrder.TryDequeue(out var oldest))
            _cache.TryRemove(oldest, out _);
    }

    public void Dispose() { /* Http is static, no per-instance disposal needed */ }
}
