namespace MaplibreNative.Routing.Core.Models;

public enum RouteProfile
{
    Auto,
    Motorcycle,
    Bicycle,
    Pedestrian,
    TrackOnly,         // pure A* on user-imported track features (e.g. snowmobile trails)
    HybridMotorcycle,  // road (Valhalla motorcycle) + trail (TrackGraphRouter)
    HybridBicycle,     // road (Valhalla bicycle)    + trail (TrackGraphRouter)
}
