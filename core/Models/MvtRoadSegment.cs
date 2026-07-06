namespace MaplibreNative.Routing.Core.Models;

internal sealed class MvtRoadSegment
{
    public required IReadOnlyList<(double Lon, double Lat)> Coordinates { get; init; }
    public required string RoadClass { get; init; }
    public string? Subclass { get; init; }
    public int Oneway { get; init; }
    public string? Surface { get; init; }
    public string? Brunnel { get; init; }
    public bool Toll { get; init; }
    public bool Ramp { get; init; }
    public bool AccessRestricted { get; init; }
    public string? Name { get; init; }
    public string? Ref { get; init; }
    public ulong FeatureId { get; init; }
}
