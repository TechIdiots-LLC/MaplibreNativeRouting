using MaplibreNative.Routing.Core.Models;

namespace MaplibreNative.Routing.Core.Graph;

/// <summary>
/// Joins vector-tile road pieces where they meet. In tiles, two roads that cross often share no vertex (simplifying
/// a straight road drops the node at the junction), a road ending on another stops beside its line rather than on
/// a vertex of it, and a tile's edge cuts a road mid-segment, each piece reaching a little into the next tile. Left
/// so, the road graph falls apart into thousands of pieces. Here a shared vertex is put into both pieces where two
/// on the same level cross, and an end lying on another piece is put onto it. A bridge over a road, or anything on
/// another <c>layer</c>, is left apart.
/// </summary>
internal static class RoadJunctions
{
    private const double CellDeg = 0.0005;   // ~50 m
    private const double TouchM = 2.0;       // an end this near another piece's line is on it
    private const double EndT = 1e-9;

    public static List<MvtRoadSegment> Connect(IReadOnlyList<MvtRoadSegment> segments)
    {
        if (segments.Count == 0) return [.. segments];
        double lat0 = segments[0].Coordinates[0].Lat * Math.PI / 180;
        double kx = Math.Cos(lat0) * 111_320, ky = 110_574;   // metres per degree, near enough over a route

        var lines = segments.Select(s => s.Coordinates.ToList()).ToList();
        var grid = new Dictionary<(int, int), List<(int S, int I)>>();
        static int Cell(double v) => (int)Math.Floor(v / CellDeg);
        double margin = TouchM / ky;
        for (int s = 0; s < lines.Count; s++)
        {
            var l = lines[s];
            for (int i = 0; i + 1 < l.Count; i++)
            {
                int x0 = Cell(Math.Min(l[i].Lon, l[i + 1].Lon) - margin), x1 = Cell(Math.Max(l[i].Lon, l[i + 1].Lon) + margin);
                int y0 = Cell(Math.Min(l[i].Lat, l[i + 1].Lat) - margin), y1 = Cell(Math.Max(l[i].Lat, l[i + 1].Lat) + margin);
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                    {
                        if (!grid.TryGetValue((x, y), out var list)) grid[(x, y)] = list = [];
                        list.Add((s, i));
                    }
            }
        }

        var inserts = new Dictionary<(int S, int I), List<(double T, (double Lon, double Lat) P)>>();
        void Insert(int s, int i, double t, (double Lon, double Lat) p)
        {
            if (t <= EndT || t >= 1 - EndT) return;   // at a vertex it has already
            if (!inserts.TryGetValue((s, i), out var list)) inserts[(s, i)] = list = [];
            list.Add((t, p));
        }

        // Crossings: a vertex both share, where two on the same level cross
        var seen = new HashSet<(int, int, int, int)>();
        foreach (var list in grid.Values)
        {
            for (int a = 0; a < list.Count; a++)
                for (int b = a + 1; b < list.Count; b++)
                {
                    var (sa, ia) = list[a];
                    var (sb, ib) = list[b];
                    if (sa == sb && Math.Abs(ia - ib) <= 1) continue;
                    if (!SameLevel(segments[sa], segments[sb])) continue;
                    if (!seen.Add((sa, ia, sb, ib))) continue;
                    var (p1, p2, q1, q2) = (lines[sa][ia], lines[sa][ia + 1], lines[sb][ib], lines[sb][ib + 1]);
                    if (Cross(p1, p2, q1, q2, kx, ky) is not (double ta, double tb)) continue;
                    var at = (p1.Lon + (p2.Lon - p1.Lon) * ta, p1.Lat + (p2.Lat - p1.Lat) * ta);
                    // Where one already has a vertex, the other takes that very point
                    if (tb <= EndT) at = q1; else if (tb >= 1 - EndT) at = q2;
                    if (ta <= EndT) at = p1; else if (ta >= 1 - EndT) at = p2;
                    Insert(sa, ia, ta, at);
                    Insert(sb, ib, tb, at);
                }
        }

        // Ends: one lying on another piece's line (a road ending on another, or a tile's edge) is put onto it
        for (int s = 0; s < lines.Count; s++)
        {
            foreach (int end in new[] { 0, lines[s].Count - 1 })
            {
                var p = lines[s][end];
                if (!grid.TryGetValue((Cell(p.Lon), Cell(p.Lat)), out var near)) continue;
                (int S, int I, double T, (double Lon, double Lat) Q, double D)? best = null;
                foreach (var (s2, i2) in near)
                {
                    if (s2 == s || !SameLevel(segments[s], segments[s2])) continue;
                    var (t, q, d) = Project(p, lines[s2][i2], lines[s2][i2 + 1], kx, ky);
                    if (d <= TouchM && (best == null || d < best.Value.D)) best = (s2, i2, t, q, d);
                }
                if (best is not { } b) continue;
                var onto = b.T <= EndT ? lines[b.S][b.I] : b.T >= 1 - EndT ? lines[b.S][b.I + 1] : b.Q;
                lines[s][end] = onto;
                Insert(b.S, b.I, b.T, onto);
            }
        }

        var result = new List<MvtRoadSegment>(segments.Count);
        for (int s = 0; s < segments.Count; s++)
        {
            var l = lines[s];
            var coords = new List<(double Lon, double Lat)>(l.Count);
            for (int i = 0; i < l.Count; i++)
            {
                coords.Add(l[i]);
                if (i + 1 < l.Count && inserts.TryGetValue((s, i), out var add))
                    coords.AddRange(add.OrderBy(x => x.T).Select(x => x.P));
            }
            var o = segments[s];
            result.Add(new MvtRoadSegment
            {
                Coordinates = coords, RoadClass = o.RoadClass, Subclass = o.Subclass, Oneway = o.Oneway,
                Surface = o.Surface, Brunnel = o.Brunnel, Layer = o.Layer, Toll = o.Toll, Ramp = o.Ramp,
                AccessRestricted = o.AccessRestricted, Name = o.Name, Ref = o.Ref, FeatureId = o.FeatureId,
            });
        }
        return result;
    }

    /// <summary>A bridge meets a bridge, a tunnel a tunnel, a road a road; and only on the same layer.</summary>
    private static bool SameLevel(MvtRoadSegment a, MvtRoadSegment b) =>
        Level(a.Brunnel) == Level(b.Brunnel) && a.Layer == b.Layer;

    private static string Level(string? brunnel) => brunnel is "bridge" or "tunnel" ? brunnel : "";

    /// <summary>Where two segments cross, as the fraction along each; null when they don't.</summary>
    private static (double, double)? Cross((double Lon, double Lat) p1, (double Lon, double Lat) p2,
        (double Lon, double Lat) q1, (double Lon, double Lat) q2, double kx, double ky)
    {
        double rx = (p2.Lon - p1.Lon) * kx, ry = (p2.Lat - p1.Lat) * ky;
        double sx = (q2.Lon - q1.Lon) * kx, sy = (q2.Lat - q1.Lat) * ky;
        double den = rx * sy - ry * sx;
        if (Math.Abs(den) < 1e-9) return null;   // parallel
        double qx = (q1.Lon - p1.Lon) * kx, qy = (q1.Lat - p1.Lat) * ky;
        double t = (qx * sy - qy * sx) / den, u = (qx * ry - qy * rx) / den;
        return t >= 0 && t <= 1 && u >= 0 && u <= 1 ? (t, u) : null;
    }

    /// <summary>The nearest point of a segment to a place: how far along, the point, and how far off in metres.</summary>
    private static (double T, (double Lon, double Lat) Q, double D) Project((double Lon, double Lat) p,
        (double Lon, double Lat) a, (double Lon, double Lat) b, double kx, double ky)
    {
        double dx = (b.Lon - a.Lon) * kx, dy = (b.Lat - a.Lat) * ky;
        double len = dx * dx + dy * dy;
        double t = len == 0 ? 0 : Math.Clamp(((p.Lon - a.Lon) * kx * dx + (p.Lat - a.Lat) * ky * dy) / len, 0, 1);
        var q = (a.Lon + (b.Lon - a.Lon) * t, a.Lat + (b.Lat - a.Lat) * t);
        return (t, q, Math.Sqrt(Math.Pow((p.Lon - q.Item1) * kx, 2) + Math.Pow((p.Lat - q.Item2) * ky, 2)));
    }
}
