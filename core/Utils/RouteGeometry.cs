namespace MaplibreNative.Routing.Core.Utils;

/// <summary>
/// Positions along a route's line: the nearest point of the line to a place (not just its nearest vertex), how far
/// along the line that is, and the point a distance along it. Distances are on a flat sheet scaled to the latitude:
/// within a metre over the few hundred metres of a segment.
/// </summary>
public static class RouteGeometry
{
    /// <summary>Where a place falls on a line.</summary>
    /// <param name="Segment">The segment it falls on: between vertex <c>Segment</c> and the next.</param>
    /// <param name="Lat">The point on the line, latitude.</param>
    /// <param name="Lon">The point on the line, longitude.</param>
    /// <param name="DistanceMeters">From the place to the line.</param>
    /// <param name="AlongMeters">From the line's start to the point, along it.</param>
    public readonly record struct Snap(int Segment, double Lat, double Lon, double DistanceMeters, double AlongMeters);

    private const double MetersPerDegLat = 110_574;
    private const double MetersPerDegLonAtEquator = 111_320;

    /// <summary>The distance along a line to each of its vertices: <c>result[i]</c> is from the start to vertex i.</summary>
    public static double[] Cumulative(IReadOnlyList<(double Lon, double Lat)> shape)
    {
        var cum = new double[shape.Count];
        for (int i = 1; i < shape.Count; i++)
            cum[i] = cum[i - 1] + RouteUtils.HaversineMeters(shape[i - 1].Lat, shape[i - 1].Lon, shape[i].Lat, shape[i].Lon);
        return cum;
    }

    /// <summary>The nearest point to a place on the segments <paramref name="from"/> to <paramref name="to"/> of a line.</summary>
    public static Snap SnapToLine(double lat, double lon, IReadOnlyList<(double Lon, double Lat)> shape,
        double[] cumulative, int from = 0, int to = int.MaxValue)
    {
        if (shape.Count == 0) return new Snap(0, lat, lon, double.MaxValue, 0);
        if (shape.Count == 1)
            return new Snap(0, shape[0].Lat, shape[0].Lon, RouteUtils.HaversineMeters(lat, lon, shape[0].Lat, shape[0].Lon), 0);
        from = Math.Clamp(from, 0, shape.Count - 2);
        to = Math.Clamp(to, from, shape.Count - 2);
        double kx = Math.Cos(lat * Math.PI / 180) * MetersPerDegLonAtEquator;
        var best = new Snap(from, shape[from].Lat, shape[from].Lon, double.MaxValue, cumulative[from]);
        for (int i = from; i <= to; i++)
        {
            var (aLon, aLat) = shape[i];
            var (bLon, bLat) = shape[i + 1];
            double dx = (bLon - aLon) * kx, dy = (bLat - aLat) * MetersPerDegLat;
            double len2 = dx * dx + dy * dy;
            double t = len2 == 0 ? 0 : Math.Clamp(((lon - aLon) * kx * dx + (lat - aLat) * MetersPerDegLat * dy) / len2, 0, 1);
            double pLon = aLon + (bLon - aLon) * t, pLat = aLat + (bLat - aLat) * t;
            double ex = (lon - pLon) * kx, ey = (lat - pLat) * MetersPerDegLat;
            double d = Math.Sqrt(ex * ex + ey * ey);
            if (d < best.DistanceMeters)
                best = new Snap(i, pLat, pLon, d, cumulative[i] + (cumulative[i + 1] - cumulative[i]) * t);
        }
        return best;
    }

    /// <summary>The point <paramref name="along"/> metres from a line's start, and the line's bearing there.</summary>
    public static (double Lat, double Lon, double Bearing) PointAlong(IReadOnlyList<(double Lon, double Lat)> shape,
        double[] cumulative, double along)
    {
        if (shape.Count == 0) return (0, 0, 0);
        if (shape.Count == 1) return (shape[0].Lat, shape[0].Lon, 0);
        along = Math.Clamp(along, 0, cumulative[^1]);
        int i = Array.BinarySearch(cumulative, along);
        if (i < 0) i = ~i - 1;
        i = Math.Clamp(i, 0, shape.Count - 2);
        double len = cumulative[i + 1] - cumulative[i];
        double t = len == 0 ? 0 : (along - cumulative[i]) / len;
        var (aLon, aLat) = shape[i];
        var (bLon, bLat) = shape[i + 1];
        return (aLat + (bLat - aLat) * t, aLon + (bLon - aLon) * t, RouteUtils.InitialBearing(aLat, aLon, bLat, bLon));
    }
}
