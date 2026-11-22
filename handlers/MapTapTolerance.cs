using MapLibreNative.Maui.Handlers;

namespace MaplibreNative.Routing;

/// <summary>How far a tap on the map may miss what it's meant for, in metres at the map's current zoom.</summary>
public static class MapTapTolerance
{
    /// <summary>
    /// <paramref name="pixels"/> screen pixels as metres at a latitude, at the map's zoom: for matching a tap to a stop
    /// (RouteWaypoints.NearestPoint) or a route line (RouteWaypoints.NearestLeg). 44 px is a finger's width.
    /// </summary>
    public static double Meters(IMapLibreMapController controller, double latitude, double pixels = 44)
    {
        // MapLibre's world is 512 × 2^zoom pixels across at the equator
        double metersPerPixel = 40_075_016.686 * Math.Cos(latitude * Math.PI / 180) / (512 * Math.Pow(2, controller.GetZoom()));
        return pixels * metersPerPixel;
    }
}
