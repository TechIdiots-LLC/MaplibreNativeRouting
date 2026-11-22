namespace MaplibreNative.Routing.Core.Mvt;

public readonly record struct TileCoord(int Z, int X, int Y)
{
    public static TileCoord FromLatLon(double lat, double lon, int zoom)
    {
        int n = 1 << zoom;
        int x = (int)Math.Floor((lon + 180.0) / 360.0 * n);
        double latRad = lat * Math.PI / 180.0;
        int y = (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);
        x = Math.Clamp(x, 0, n - 1);
        y = Math.Clamp(y, 0, n - 1);
        return new TileCoord(zoom, x, y);
    }

    public (double North, double South, double West, double East) GetBounds()
    {
        int n = 1 << Z;
        double west = (double)X / n * 360.0 - 180.0;
        double east = (double)(X + 1) / n * 360.0 - 180.0;
        double north = MercatorYToLat((double)Y / n);
        double south = MercatorYToLat((double)(Y + 1) / n);
        return (north, south, west, east);
    }

    public (double Lat, double Lon) FeatureToLatLon(int fx, int fy, uint extent) => FeatureToLatLon((double)fx, fy, extent);

    /// <summary>A point in tile units, which may fall between them (a line clipped at the tile's edge).</summary>
    public (double Lat, double Lon) FeatureToLatLon(double fx, double fy, uint extent)
    {
        int n = 1 << Z;
        double lonFrac = (double)fx / extent;
        double latFrac = (double)fy / extent;

        double west = (double)X / n * 360.0 - 180.0;
        double east = (double)(X + 1) / n * 360.0 - 180.0;
        double lon = west + lonFrac * (east - west);

        double yTop = (double)Y / n;
        double yBottom = (double)(Y + 1) / n;
        double yFrac = yTop + latFrac * (yBottom - yTop);
        double lat = MercatorYToLat(yFrac);

        return (lat, lon);
    }

    public static List<TileCoord> CoverBoundingBox(double minLat, double minLon, double maxLat, double maxLon, int zoom)
    {
        var tl = FromLatLon(maxLat, minLon, zoom);
        var br = FromLatLon(minLat, maxLon, zoom);
        var tiles = new List<TileCoord>();
        for (int y = tl.Y; y <= br.Y; y++)
            for (int x = tl.X; x <= br.X; x++)
                tiles.Add(new TileCoord(zoom, x, y));
        return tiles;
    }

    /// <summary>
    /// The tiles within <paramref name="widthMeters"/> of the line from A to B: a corridor rather than the box round
    /// it, which on a diagonal trip is about twice the tiles, all of them away from the way.
    /// </summary>
    public static List<TileCoord> CoverCorridor(double aLat, double aLon, double bLat, double bLon, double widthMeters, int zoom)
    {
        // Metres on a plane at the trip's middle latitude: near enough over a trip's length
        double kx = Math.Cos((aLat + bLat) / 2 * Math.PI / 180) * 111_320, ky = 110_574;
        double dLat = widthMeters / ky, dLon = widthMeters / kx;
        double bx = (bLon - aLon) * kx, by = (bLat - aLat) * ky, len2 = bx * bx + by * by;
        return CoverBoundingBox(Math.Min(aLat, bLat) - dLat, Math.Min(aLon, bLon) - dLon,
                Math.Max(aLat, bLat) + dLat, Math.Max(aLon, bLon) + dLon, zoom)
            .Where(t =>
            {
                var (n, s, w, e) = t.GetBounds();
                double cx = ((w + e) / 2 - aLon) * kx, cy = ((n + s) / 2 - aLat) * ky;
                double halfDiagonal = Math.Sqrt(Math.Pow((e - w) * kx, 2) + Math.Pow((n - s) * ky, 2)) / 2;
                double f = len2 > 0 ? Math.Clamp((cx * bx + cy * by) / len2, 0, 1) : 0;
                double dx = cx - f * bx, dy = cy - f * by;
                return Math.Sqrt(dx * dx + dy * dy) <= widthMeters + halfDiagonal;
            })
            .ToList();
    }

    private static double MercatorYToLat(double yFrac)
    {
        double mercN = Math.PI * (1 - 2 * yFrac);
        return Math.Atan(Math.Sinh(mercN)) * 180.0 / Math.PI;
    }
}
