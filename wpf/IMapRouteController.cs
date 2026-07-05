namespace MaplibreNative.Routing.Wpf;

/// <summary>
/// Minimal map controller interface used by RouteOverlay. Implement this as a thin
/// wrapper over your WPF map control (e.g. MlnMapImage from MaplibreNativeMAUI).
/// </summary>
public interface IMapRouteController
{
    void AddGeoJsonSource(string sourceId, string geoJson);
    void SetGeoJsonSource(string sourceId, string geoJson);
    void AddLineLayer(string layerId, string sourceId, string? belowLayerId, string? sourceLayer,
        IDictionary<string, object?> properties);
    void RemoveLayer(string layerId);
    void RemoveSource(string sourceId);
}
