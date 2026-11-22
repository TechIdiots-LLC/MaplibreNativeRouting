using MaplibreNative.Routing.Core.Graph;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Mvt;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Routing;

internal static class MvtGraphBuilder
{
    private const int RoutingZoom = 14;
    // A road piece lies along the named line it's part of: the two come from the same ways, drawn a little apart
    private const double NameToleranceM = 12;

    public static async Task<RoadGraph> BuildAsync(
        TileProvider tiles,
        double originLat, double originLon,
        double destLat, double destLon,
        MvtCostingModel costing,
        CancellationToken ct = default,
        IProgress<string>? progress = null,
        IReadOnlyList<RouteClosure>? closures = null)
    {
        var segments = await LoadSegmentsAsync(tiles, originLat, originLon, destLat, destLon, ct, progress).ConfigureAwait(false);
        progress?.Report("Building road graph…");
        return RoadGraph.Build(RoadJunctions.Connect(RoadJunctions.ApplyClosures(segments, closures ?? [])), costing, closures);
    }

    /// <summary>The road pieces of the tiles around the corridor from the origin to the destination.</summary>
    public static async Task<List<MvtRoadSegment>> LoadSegmentsAsync(
        TileProvider tiles,
        double originLat, double originLon,
        double destLat, double destLon,
        CancellationToken ct = default,
        IProgress<string>? progress = null)
    {
        var tileData = await tiles.GetCorridorTilesAsync(
            originLat, originLon, destLat, destLon, RoutingZoom, ct, progress)
            .ConfigureAwait(false);

        progress?.Report($"Parsing {tileData.Count} tile(s)…");
        var segments = new List<MvtRoadSegment>();
        foreach (var (coord, data) in tileData)
        {
            ct.ThrowIfCancellationRequested();
            var mvtTile = MvtDecoder.Decode(data);
            ExtractSegments(mvtTile, coord, segments);
        }
        return segments;
    }

    public static async Task<RoadGraph> BuildExpandedAsync(
        TileProvider tiles,
        double originLat, double originLon,
        double destLat, double destLon,
        MvtCostingModel costing,
        double expansionFactor,
        CancellationToken ct = default,
        IProgress<string>? progress = null)
    {
        var tileData = await tiles.GetCorridorTilesAsync(
            originLat, originLon, destLat, destLon, RoutingZoom, ct, progress)
            .ConfigureAwait(false);

        progress?.Report($"Parsing {tileData.Count} tile(s)…");
        var segments = new List<MvtRoadSegment>();
        foreach (var (coord, data) in tileData)
        {
            ct.ThrowIfCancellationRequested();
            var mvtTile = MvtDecoder.Decode(data);
            ExtractSegments(mvtTile, coord, segments);
        }

        progress?.Report("Building road graph…");
        return RoadGraph.Build(RoadJunctions.Connect(segments), costing);
    }

    private static void ExtractSegments(MvtTile mvtTile, TileCoord coord, List<MvtRoadSegment> segments)
    {
        var transportLayer = mvtTile.GetLayer("transportation");
        if (transportLayer == null) return;

        var names = new NameIndex(mvtTile.GetLayer("transportation_name"), coord, NameToleranceM);

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
            int layer = props.TryGetValue("layer", out var ly) ? Convert.ToInt32(ly) : 0;

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

                var named = names.Find(ring, roadClass, transportLayer.Extent);

                segments.Add(new MvtRoadSegment
                {
                    Coordinates = coords,
                    RoadClass = roadClass,
                    Subclass = subclass,
                    Oneway = oneway,
                    Surface = surface,
                    Brunnel = brunnel,
                    Layer = layer,
                    Toll = toll,
                    Ramp = ramp,
                    AccessRestricted = access,
                    Name = named?.Name,
                    Ref = named?.Ref,
                    FeatureId = feature.Id,
                });
            }
        }
    }

    /// <summary>
    /// A tile's named lines (transportation_name), by where they run: OpenMapTiles names roads on lines of their own,
    /// merged across many of the transportation layer's pieces, so a piece is named by the line it lies along rather
    /// than by sharing an end with one. Worked in tile units.
    /// </summary>
    internal sealed class NameIndex
    {
        private const int Cell = 128;
        private readonly List<(string? Name, string? Ref, string? Class, List<(int X, int Y)> Points)> _lines = [];
        private readonly Dictionary<(int, int), List<(int Line, int Seg)>> _grid = [];
        private readonly double _tolerance;
        private readonly uint _extent = 4096;

        public NameIndex(MvtLayer? layer, TileCoord coord, double toleranceMeters)
        {
            if (layer == null) return;
            _extent = layer.Extent;
            var (north, south, _, _) = coord.GetBounds();
            double metersPerUnit = 40075016.686 * Math.Cos((north + south) / 2 * Math.PI / 180) / ((1 << coord.Z) * (double)layer.Extent);
            _tolerance = toleranceMeters / metersPerUnit;

            foreach (var feature in layer.Features)
            {
                if (feature.Type != MvtGeomType.LineString) continue;
                var props = MvtDecoder.GetProperties(feature, layer);
                string? name = props.TryGetValue("name", out var n) && n?.ToString() is { Length: > 0 } ns ? ns : null;
                string? @ref = RoadNames.FormatRef(props);
                if (name == null && @ref == null) continue;
                string? cls = props.TryGetValue("class", out var c) ? c?.ToString() : null;
                foreach (var ring in feature.Geometry)
                {
                    if (ring.Count < 2) continue;
                    int line = _lines.Count;
                    _lines.Add((name, @ref, cls, ring));
                    for (int i = 0; i + 1 < ring.Count; i++)
                    {
                        int x0 = Floor(Math.Min(ring[i].X, ring[i + 1].X) - _tolerance), x1 = Floor(Math.Max(ring[i].X, ring[i + 1].X) + _tolerance);
                        int y0 = Floor(Math.Min(ring[i].Y, ring[i + 1].Y) - _tolerance), y1 = Floor(Math.Max(ring[i].Y, ring[i + 1].Y) + _tolerance);
                        for (int gx = x0; gx <= x1; gx++)
                            for (int gy = y0; gy <= y1; gy++)
                            {
                                if (!_grid.TryGetValue((gx, gy), out var list)) _grid[(gx, gy)] = list = [];
                                list.Add((line, i));
                            }
                    }
                }
            }
        }

        private static int Floor(double v) => (int)Math.Floor(v / Cell);

        /// <summary>
        /// The name and number of the named line a road piece lies along: tried at its middle, then a quarter and
        /// three quarters along; one of the same class first, when two are as near.
        /// </summary>
        /// <param name="piece">The piece, in its own layer's tile units.</param>
        /// <param name="roadClass">Its class, as the transportation layer gives it.</param>
        /// <param name="pieceExtent">Its layer's extent.</param>
        public (string? Name, string? Ref)? Find(List<(int X, int Y)> piece, string roadClass, uint pieceExtent)
        {
            if (_lines.Count == 0 || piece.Count < 2) return null;
            double scale = (double)_extent / pieceExtent;
            foreach (double at in (ReadOnlySpan<double>)[0.5, 0.25, 0.75])
            {
                var (px, py) = Along(piece, at);
                px *= scale;
                py *= scale;
                if (!_grid.TryGetValue((Floor(px), Floor(py)), out var near)) continue;
                double best = double.MaxValue;
                int bestLine = -1;
                var (dx, dy) = Direction(piece, at);
                foreach (var (line, seg) in near)
                {
                    var pts = _lines[line].Points;
                    double d = SegmentDistance(px, py, pts[seg], pts[seg + 1]);
                    if (d > _tolerance) continue;
                    // Running alongside it, not across: at a junction, the crossing street's line is as near
                    double sx = pts[seg + 1].X - pts[seg].X, sy = pts[seg + 1].Y - pts[seg].Y;
                    double slen = Math.Sqrt(sx * sx + sy * sy);
                    if (slen > 0 && Math.Abs(dx * sx + dy * sy) / slen < MinParallel) continue;
                    // As near, but another class: a bridge over it, or a road beside it
                    if (_lines[line].Class != roadClass) d += _tolerance / 2;
                    if (d < best) (best, bestLine) = (d, line);
                }
                if (bestLine >= 0) return (_lines[bestLine].Name, _lines[bestLine].Ref);
            }
            return null;
        }

        private const double MinParallel = 0.9;   // cos 25°

        /// <summary>The unit direction of a piece at a fraction along it.</summary>
        private static (double X, double Y) Direction(List<(int X, int Y)> pts, double fraction)
        {
            var (ax, ay) = Along(pts, Math.Max(0, fraction - 0.02));
            var (bx, by) = Along(pts, Math.Min(1, fraction + 0.02));
            double len = Math.Sqrt(Sq(bx - ax) + Sq(by - ay));
            if (len == 0) { ax = pts[0].X; ay = pts[0].Y; bx = pts[^1].X; by = pts[^1].Y; len = Math.Sqrt(Sq(bx - ax) + Sq(by - ay)); }
            return len == 0 ? (1, 0) : ((bx - ax) / len, (by - ay) / len);
        }

        private static (double X, double Y) Along(List<(int X, int Y)> pts, double fraction)
        {
            double total = 0;
            for (int i = 0; i + 1 < pts.Count; i++) total += Math.Sqrt(Sq(pts[i + 1].X - pts[i].X) + Sq(pts[i + 1].Y - pts[i].Y));
            double want = total * fraction;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                double len = Math.Sqrt(Sq(pts[i + 1].X - pts[i].X) + Sq(pts[i + 1].Y - pts[i].Y));
                if (want <= len && len > 0)
                {
                    double t = want / len;
                    return (pts[i].X + (pts[i + 1].X - pts[i].X) * t, pts[i].Y + (pts[i + 1].Y - pts[i].Y) * t);
                }
                want -= len;
            }
            return (pts[^1].X, pts[^1].Y);
        }

        private static double SegmentDistance(double px, double py, (int X, int Y) a, (int X, int Y) b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = dx * dx + dy * dy;
            double t = len == 0 ? 0 : Math.Clamp(((px - a.X) * dx + (py - a.Y) * dy) / len, 0, 1);
            return Math.Sqrt(Sq(px - (a.X + dx * t)) + Sq(py - (a.Y + dy * t)));
        }

        private static double Sq(double v) => v * v;
    }
}
