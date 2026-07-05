namespace MaplibreNative.Routing.Core.Models;

/// <summary>A single track feature (LineString) contributed by the host app for
/// use in TrackGraphRouter. Coordinate arrays are [lon, lat] or [lon, lat, ele].</summary>
public class TrackFeature
{
    public required IReadOnlyList<(double Lon, double Lat)> Coordinates { get; init; }
    public string? Name { get; init; }
    public string? FeatureId { get; init; }
}
