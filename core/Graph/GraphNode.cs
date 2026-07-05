using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Graph;

/// <summary>A node (intersection or endpoint) in the spatial graph.</summary>
public class GraphNode
{
    public int Id { get; }
    public double Lat { get; }
    public double Lon { get; }

    public GraphNode(int id, double lat, double lon) { Id = id; Lat = lat; Lon = lon; }
}
