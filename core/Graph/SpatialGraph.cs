using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Graph;

/// <summary>
/// Builds an undirected spatial graph from TrackFeature (LineString) coordinate arrays.
/// Nodes within MergeRadiusM of each other are merged into a single node so that
/// trail intersections and nearby endpoints connect correctly.
/// </summary>
public class SpatialGraph
{
    private const double MergeRadiusM = 15;

    public IReadOnlyList<GraphNode> Nodes { get; }
    public IReadOnlyDictionary<int, List<GraphEdge>> Adjacency { get; }

    private SpatialGraph(List<GraphNode> nodes, Dictionary<int, List<GraphEdge>> adj)
    {
        Nodes = nodes;
        Adjacency = adj;
    }

    public static SpatialGraph Build(IReadOnlyList<TrackFeature> features)
    {
        var nodes = new List<GraphNode>();
        var adj = new Dictionary<int, List<GraphEdge>>();

        int GetOrCreateNode(double lat, double lon)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (RouteUtils.HaversineMeters(lat, lon, nodes[i].Lat, nodes[i].Lon) <= MergeRadiusM)
                    return nodes[i].Id;
            }
            var node = new GraphNode(nodes.Count, lat, lon);
            nodes.Add(node);
            adj[node.Id] = new List<GraphEdge>();
            return node.Id;
        }

        void AddEdge(int from, int to, double distM, string? fid)
        {
            adj[from].Add(new GraphEdge(from, to, distM, fid));
            adj[to].Add(new GraphEdge(to, from, distM, fid));   // undirected
        }

        foreach (var feature in features)
        {
            var coords = feature.Coordinates;
            if (coords.Count < 2) continue;

            int prevId = GetOrCreateNode(coords[0].Lat, coords[0].Lon);
            for (int i = 1; i < coords.Count; i++)
            {
                int currId = GetOrCreateNode(coords[i].Lat, coords[i].Lon);
                if (currId == prevId) { continue; }

                var distM = RouteUtils.HaversineMeters(
                    coords[i - 1].Lat, coords[i - 1].Lon,
                    coords[i].Lat, coords[i].Lon);
                AddEdge(prevId, currId, distM, feature.FeatureId);
                prevId = currId;
            }
        }

        return new SpatialGraph(nodes, adj);
    }

    /// <summary>Returns the node closest to the given coordinate, or null if the graph
    /// has no nodes.</summary>
    public GraphNode? NearestNode(double lat, double lon)
    {
        GraphNode? best = null;
        double bestDist = double.MaxValue;
        foreach (var n in Nodes)
        {
            var d = RouteUtils.HaversineMeters(lat, lon, n.Lat, n.Lon);
            if (d < bestDist) { bestDist = d; best = n; }
        }
        return best;
    }
}
