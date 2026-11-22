using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Models;

/// <summary>
/// A place no route may pass: a closed trail, a washed-out road. Everything within <paramref name="RadiusMeters"/>
/// of it is left out of the routes planned, trails and roads alike.
/// </summary>
public readonly record struct RouteClosure(double Lat, double Lon, double RadiusMeters = 30)
{
    /// <summary>Whether a stretch of line passes within the closure.</summary>
    public bool Blocks((double Lon, double Lat) a, (double Lon, double Lat) b)
    {
        var shape = new[] { a, b };
        return RouteGeometry.SnapToLine(Lat, Lon, shape, RouteGeometry.Cumulative(shape)).DistanceMeters <= RadiusMeters;
    }

    /// <summary>Whether any of the closures blocks a stretch of line.</summary>
    public static bool AnyBlocks(IReadOnlyList<RouteClosure> closures, (double Lon, double Lat) a, (double Lon, double Lat) b)
    {
        foreach (var c in closures)
        {
            // A stretch whose box is clear of the closure's can't pass through it: the exact test only for the rest
            double dLat = c.RadiusMeters / 110_574, dLon = c.RadiusMeters / (111_320 * Math.Max(0.05, Math.Cos(c.Lat * Math.PI / 180)));
            if (Math.Max(a.Lat, b.Lat) < c.Lat - dLat || Math.Min(a.Lat, b.Lat) > c.Lat + dLat ||
                Math.Max(a.Lon, b.Lon) < c.Lon - dLon || Math.Min(a.Lon, b.Lon) > c.Lon + dLon) continue;
            if (c.Blocks(a, b)) return true;
        }
        return false;
    }

    /// <summary>Track features with the closed stretches cut out: a feature through a closure becomes two.</summary>
    public static IReadOnlyList<TrackFeature> Apply(IReadOnlyList<TrackFeature> features, IReadOnlyList<RouteClosure> closures)
    {
        if (closures.Count == 0) return features;
        var open = new List<TrackFeature>(features.Count);
        foreach (var f in features)
            foreach (var piece in Cut(f.Coordinates, closures))
                open.Add(new TrackFeature { Coordinates = piece, Name = f.Name, FeatureId = f.FeatureId });
        return open;
    }

    /// <summary>
    /// A line with what lies within the closures cut out, exactly at their edges: a segment a kilometre long loses
    /// only the closure's few metres, not the whole segment.
    /// </summary>
    public static List<List<(double Lon, double Lat)>> Cut(IReadOnlyList<(double Lon, double Lat)> line, IReadOnlyList<RouteClosure> closures)
    {
        var pieces = new List<List<(double Lon, double Lat)>>();
        var piece = new List<(double Lon, double Lat)>();
        void Flush()
        {
            if (piece.Count >= 2) pieces.Add(piece);
            piece = [];
        }
        for (int i = 0; i + 1 < line.Count; i++)
        {
            var (a, b) = (line[i], line[i + 1]);
            foreach (var (from, to) in Open(a, b, closures))
            {
                // A part starting after a is past a closure: a new piece
                if (from > 0) { Flush(); piece.Add(Lerp(a, b, from)); }
                else if (piece.Count == 0) piece.Add(a);
                piece.Add(to >= 1 ? b : Lerp(a, b, to));
                if (to < 1) Flush();   // into a closure
            }
        }
        Flush();
        return pieces;
    }

    /// <summary>The parts of a to b outside every closure, as fractions along it, in order.</summary>
    private static List<(double From, double To)> Open((double Lon, double Lat) a, (double Lon, double Lat) b, IReadOnlyList<RouteClosure> closures)
    {
        if (!AnyBlocks(closures, a, b)) return [(0, 1)];
        var closed = closures.Select(c => c.Inside(a, b)).Where(x => x is not null).Select(x => x!.Value).OrderBy(x => x.From);
        var open = new List<(double From, double To)>();
        double t = 0;
        foreach (var (from, to) in closed)
        {
            if (from > t) open.Add((t, from));
            t = Math.Max(t, to);
        }
        if (t < 1) open.Add((t, 1));
        return open;
    }

    private static (double Lon, double Lat) Lerp((double Lon, double Lat) a, (double Lon, double Lat) b, double t) =>
        (a.Lon + (b.Lon - a.Lon) * t, a.Lat + (b.Lat - a.Lat) * t);

    /// <summary>The part of a to b within this closure, as fractions along it; null when none is.</summary>
    private (double From, double To)? Inside((double Lon, double Lat) a, (double Lon, double Lat) b)
    {
        double kx = Math.Cos(Lat * Math.PI / 180) * 111_320, ky = 110_574;
        double ax = (a.Lon - Lon) * kx, ay = (a.Lat - Lat) * ky;
        double dx = (b.Lon - a.Lon) * kx, dy = (b.Lat - a.Lat) * ky;
        double qa = dx * dx + dy * dy, qb = 2 * (ax * dx + ay * dy), qc = ax * ax + ay * ay - RadiusMeters * RadiusMeters;
        if (qa == 0) return qc <= 0 ? (0, 1) : null;
        double disc = qb * qb - 4 * qa * qc;
        if (disc < 0) return null;
        double s = Math.Sqrt(disc);
        double t0 = Math.Max(0, (-qb - s) / (2 * qa)), t1 = Math.Min(1, (-qb + s) / (2 * qa));
        return t0 < t1 ? (t0, t1) : null;
    }

    /// <summary>The closure as a polygon (an octagon around it), [lon, lat], for a route service's excluded areas.</summary>
    public IReadOnlyList<(double Lon, double Lat)> ToPolygon()
    {
        double dLat = RadiusMeters / 110_574, dLon = RadiusMeters / (111_320 * Math.Max(0.05, Math.Cos(Lat * Math.PI / 180)));
        var ring = new List<(double Lon, double Lat)>(9);
        for (int i = 0; i <= 8; i++)
        {
            double a = Math.PI * 2 * (i % 8) / 8;
            ring.Add((Lon + dLon * Math.Sin(a), Lat + dLat * Math.Cos(a)));
        }
        return ring;
    }
}
