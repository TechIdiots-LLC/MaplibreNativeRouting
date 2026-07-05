using MaplibreNative.Routing.Core.Models;

namespace MaplibreNative.Routing;

/// <summary>Implemented by the host app to provide track features (visible layers
/// with "use for routing" enabled) to the routing engine.</summary>
public interface IRouteDataSource
{
    /// <summary>Returns the current set of track features that may be used for routing.</summary>
    Task<IReadOnlyList<TrackFeature>> GetRoutableTrackFeaturesAsync();
}
