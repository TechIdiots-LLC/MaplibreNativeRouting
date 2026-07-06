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
    OfflineAuto,       // offline MVT routing — auto costing
    OfflineBicycle,    // offline MVT routing — bicycle costing
    OfflinePedestrian, // offline MVT routing — pedestrian costing
    HybridOfflineMotorcycle, // trail (GeoJSON) primary + MVT road gap-fill
    HybridOfflineBicycle,    // trail (GeoJSON) primary + MVT road gap-fill
}
