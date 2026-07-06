using MaplibreNative.Routing.Core.Models;

namespace MaplibreNative.Routing.Core.Utils;

/// <summary>Highway detection and route annotation utilities.</summary>
public static class RouteUtils
{
    private static readonly HashSet<string> HighwayClasses =
        new(StringComparer.OrdinalIgnoreCase) { "motorway", "trunk" };

    /// <summary>Checks all legs/steps for highway-class road segments, attaches a
    /// HighwayWarning to the route if any are found, and returns the annotated route.</summary>
    public static DirectionsRoute AttachHighwayWarning(DirectionsRoute route)
    {
        var indices = new List<int>();
        double totalHighwayDist = 0;

        // Check each leg independently; warnings index into leg 0 for single-destination routes.
        for (int li = 0; li < route.Legs.Count; li++)
        {
            var leg = route.Legs[li];
            for (int si = 0; si < leg.Steps.Count; si++)
            {
                var step = leg.Steps[si];
                if (IsHighwayStep(step))
                {
                    indices.Add(si);
                    totalHighwayDist += step.Distance;
                }
            }
        }

        if (indices.Count == 0) return route;

        return route with
        {
            HighwayWarning = new HighwayWarning
            {
                HighwayStepIndices = indices,
                TotalHighwayDistanceMeters = totalHighwayDist,
            }
        };
    }

    /// <summary>True if the step traverses a motorway or trunk road.</summary>
    public static bool IsHighwayStep(LegStep step)
        => step.RoadClass is not null && HighwayClasses.Contains(step.RoadClass);

    /// <summary>Haversine distance in meters between two WGS-84 points.</summary>
    public static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    public static double InitialBearing(double lat1, double lon1, double lat2, double lon2)
    {
        double dLon = (lon2 - lon1) * Math.PI / 180;
        double lat1R = lat1 * Math.PI / 180;
        double lat2R = lat2 * Math.PI / 180;
        double y = Math.Sin(dLon) * Math.Cos(lat2R);
        double x = Math.Cos(lat1R) * Math.Sin(lat2R) -
                   Math.Sin(lat1R) * Math.Cos(lat2R) * Math.Cos(dLon);
        double bearing = Math.Atan2(y, x) * 180 / Math.PI;
        return (bearing + 360) % 360;
    }

    public static double TurnAngle(double prevBearing, double nextBearing)
    {
        double diff = nextBearing - prevBearing;
        if (diff > 180) diff -= 360;
        if (diff < -180) diff += 360;
        return diff;
    }

    /// <summary>Returns the index of the shape coordinate closest to the given point.</summary>
    public static int SnapToShape(
        double lat, double lon,
        IReadOnlyList<(double Lon, double Lat)> shape,
        out double snappedLon, out double snappedLat)
    {
        int best = 0;
        double bestDist = double.MaxValue;
        for (int i = 0; i < shape.Count; i++)
        {
            var d = HaversineMeters(lat, lon, shape[i].Lat, shape[i].Lon);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        snappedLon = shape[best].Lon;
        snappedLat = shape[best].Lat;
        return best;
    }
}
