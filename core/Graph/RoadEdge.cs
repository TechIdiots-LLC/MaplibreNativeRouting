namespace MaplibreNative.Routing.Core.Graph;

public sealed class RoadEdge
{
    public int FromNodeId { get; }
    public int ToNodeId { get; }
    public double DistanceMeters { get; }
    public double Cost { get; internal set; }

    public string RoadClass { get; }
    public string? Subclass { get; }
    public string? Surface { get; }
    public string? StreetName { get; }
    public string? Ref { get; }
    public bool IsToll { get; }
    public bool IsRamp { get; }
    public bool IsBridge { get; }
    public bool IsTunnel { get; }
    public double SpeedKmh { get; }

    public IReadOnlyList<(double Lon, double Lat)> IntermediatePoints { get; }

    public RoadEdge(
        int fromNodeId, int toNodeId, double distanceMeters,
        string roadClass, string? subclass, string? surface,
        string? streetName, string? @ref,
        bool isToll, bool isRamp, bool isBridge, bool isTunnel,
        double speedKmh,
        IReadOnlyList<(double Lon, double Lat)> intermediatePoints)
    {
        FromNodeId = fromNodeId;
        ToNodeId = toNodeId;
        DistanceMeters = distanceMeters;
        RoadClass = roadClass;
        Subclass = subclass;
        Surface = surface;
        StreetName = streetName;
        Ref = @ref;
        IsToll = isToll;
        IsRamp = isRamp;
        IsBridge = isBridge;
        IsTunnel = isTunnel;
        SpeedKmh = speedKmh;
        IntermediatePoints = intermediatePoints;
    }
}
