namespace MaplibreNative.Routing.Core.Graph;

/// <summary>A directed edge between two graph nodes.</summary>
public class GraphEdge
{
    public int FromNodeId { get; }
    public int ToNodeId { get; }
    public double DistanceMeters { get; }
    public string? FeatureId { get; }

    public GraphEdge(int from, int to, double distM, string? featureId = null)
    {
        FromNodeId = from;
        ToNodeId = to;
        DistanceMeters = distM;
        FeatureId = featureId;
    }
}
