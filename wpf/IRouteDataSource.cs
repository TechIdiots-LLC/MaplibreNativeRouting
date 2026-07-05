using MaplibreNative.Routing.Core.Models;

namespace MaplibreNative.Routing.Wpf;

/// <summary>Implemented by the host app to provide track features (visible layers
/// with "use for routing" enabled) to the routing engine.</summary>
public interface IRouteDataSource
{
    Task<IReadOnlyList<TrackFeature>> GetRoutableTrackFeaturesAsync();
}
