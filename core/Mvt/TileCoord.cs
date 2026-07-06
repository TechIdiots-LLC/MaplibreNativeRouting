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

    public (double Lat, double Lon) FeatureToLatLon(int fx, int fy, uint extent)
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

    private static double MercatorYToLat(double yFrac)
    {
        double mercN = Math.PI * (1 - 2 * yFrac);
        return Math.Atan(Math.Sinh(mercN)) * 180.0 / Math.PI;
    }
}
