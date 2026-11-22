using MaplibreNative.Routing.Core.Models;

namespace MaplibreNative.Routing.Core.Routing;

/// <summary>The routing engine for each profile.</summary>
public static class RoutingEngines
{
    public static IRoutingEngine For(RouteProfile profile) => profile switch
    {
        RouteProfile.TrackOnly => new TrackGraphRouter(),
        RouteProfile.HybridMotorcycle or RouteProfile.HybridBicycle => new HybridRouter(),
        RouteProfile.HybridOfflineMotorcycle or RouteProfile.HybridOfflineBicycle => new HybridRouter(new MvtRouter()),
        RouteProfile.OfflineAuto or RouteProfile.OfflineBicycle or RouteProfile.OfflinePedestrian => new MvtRouter(),
        _ => new ValhallaMtbRouter(),
    };
}
