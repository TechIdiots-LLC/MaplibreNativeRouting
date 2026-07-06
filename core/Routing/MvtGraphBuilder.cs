using MaplibreNative.Routing.Core.Graph;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Mvt;

namespace MaplibreNative.Routing.Core.Routing;

internal static class MvtGraphBuilder
{
    private const int RoutingZoom = 14;

    public static async Task<RoadGraph> BuildAsync(
        TileProvider tiles,
        double originLat, double originLon,
        double destLat, double destLon,
        MvtCostingModel costing,
        CancellationToken ct = default)
    {
        var tileData = await tiles.GetCorridorTilesAsync(
            originLat, originLon, destLat, destLon, RoutingZoom, ct)
            .ConfigureAwait(false);

        var segments = new List<MvtRoadSegment>();

        foreach (var (coord, data) in tileData)
        {
            ct.ThrowIfCancellationRequested();
            var mvtTile = MvtDecoder.Decode(data);
            ExtractSegments(mvtTile, coord, segments);
        }

        return RoadGraph.Build(segments, costing);
    }

    public static async Task<RoadGraph> BuildExpandedAsync(
        TileProvider tiles,
        double originLat, double originLon,
        double destLat, double destLon,
        MvtCostingModel costing,
        double expansionFactor,
        CancellationToken ct = default)
    {
        var tileData = await tiles.GetCorridorTilesAsync(
            originLat, originLon, destLat, destLon, RoutingZoom, ct)
            .ConfigureAwait(false);

        var segments = new List<MvtRoadSegment>();
        foreach (var (coord, data) in tileData)
        {
            ct.ThrowIfCancellationRequested();
            var mvtTile = MvtDecoder.Decode(data);
            ExtractSegments(mvtTile, coord, segments);
        }

        return RoadGraph.Build(segments, costing);
    }

    private static void ExtractSegments(MvtTile mvtTile, TileCoord coord, List<MvtRoadSegment> segments)
    {
        var transportLayer = mvtTile.GetLayer("transportation");
        if (transportLayer == null) return;

        var nameLayer = mvtTile.GetLayer("transportation_name");
        var nameLookup = BuildNameLookup(nameLayer, coord);

        foreach (var feature in transportLayer.Features)
        {
            if (feature.Type != MvtGeomType.LineString) continue;
            if (feature.Geometry.Count == 0) continue;

            var props = MvtDecoder.GetProperties(feature, transportLayer);

            if (!props.TryGetValue("class", out var classObj)) continue;
            string roadClass = classObj.ToString() ?? "";
            if (string.IsNullOrEmpty(roadClass)) continue;

            string? subclass = props.TryGetValue("subclass", out var sc) ? sc.ToString() : null;
            string? surface = props.TryGetValue("surface", out var sf) ? sf.ToString() : null;
            string? brunnel = props.TryGetValue("brunnel", out var br) ? br.ToString() : null;

            int oneway = 0;
            if (props.TryGetValue("oneway", out var ow))
                oneway = Convert.ToInt32(ow);

            bool toll = props.TryGetValue("toll", out var t) && Convert.ToInt32(t) != 0;
            bool ramp = props.TryGetValue("ramp", out var r) && Convert.ToInt32(r) != 0;
            bool access = props.TryGetValue("access", out var a) && a is "no";

            foreach (var ring in feature.Geometry)
            {
                if (ring.Count < 2) continue;

                var coords = new List<(double Lon, double Lat)>(ring.Count);
                foreach (var (fx, fy) in ring)
                {
                    var (lat, lon) = coord.FeatureToLatLon(fx, fy, transportLayer.Extent);
                    coords.Add((lon, lat));
                }

                string? name = FindStreetName(nameLookup, coords);

                segments.Add(new MvtRoadSegment
                {
                    Coordinates = coords,
                    RoadClass = roadClass,
                    Subclass = subclass,
                    Oneway = oneway,
                    Surface = surface,
                    Brunnel = brunnel,
                    Toll = toll,
                    Ramp = ramp,
                    AccessRestricted = access,
                    Name = name,
                    FeatureId = feature.Id,
                });
            }
        }
    }

    private static Dictionary<(double Lon, double Lat), string> BuildNameLookup(
        MvtLayer? nameLayer, TileCoord coord)
    {
        var lookup = new Dictionary<(double, double), string>();
        if (nameLayer == null) return lookup;

        foreach (var feature in nameLayer.Features)
        {
            if (feature.Type != MvtGeomType.LineString) continue;

            var props = MvtDecoder.GetProperties(feature, nameLayer);
            string? name = null;
            if (props.TryGetValue("name", out var n)) name = n.ToString();
            if (string.IsNullOrEmpty(name) && props.TryGetValue("ref", out var refVal))
                name = refVal.ToString();
            if (string.IsNullOrEmpty(name)) continue;

            foreach (var ring in feature.Geometry)
            {
                if (ring.Count == 0) continue;
                var (fx, fy) = ring[0];
                var (lat, lon) = coord.FeatureToLatLon(fx, fy, nameLayer.Extent);
                var key = (Math.Round(lon, 6), Math.Round(lat, 6));
                lookup.TryAdd(key, name!);

                if (ring.Count > 1)
                {
                    var (fx2, fy2) = ring[^1];
                    var (lat2, lon2) = coord.FeatureToLatLon(fx2, fy2, nameLayer.Extent);
                    var key2 = (Math.Round(lon2, 6), Math.Round(lat2, 6));
                    lookup.TryAdd(key2, name!);
                }
            }
        }

        return lookup;
    }

    private static string? FindStreetName(
        Dictionary<(double Lon, double Lat), string> nameLookup,
        List<(double Lon, double Lat)> coords)
    {
        if (nameLookup.Count == 0 || coords.Count == 0) return null;

        var startKey = (Math.Round(coords[0].Lon, 6), Math.Round(coords[0].Lat, 6));
        if (nameLookup.TryGetValue(startKey, out var name)) return name;

        var endKey = (Math.Round(coords[^1].Lon, 6), Math.Round(coords[^1].Lat, 6));
        if (nameLookup.TryGetValue(endKey, out name)) return name;

        return null;
    }
}
