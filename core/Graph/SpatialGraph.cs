using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Graph;

/// <summary>
/// Builds an undirected spatial graph from TrackFeature (LineString) coordinate arrays.
/// Segment endpoints use a larger merge radius so that nearby trail segment ends connect
/// into a single node; interior coordinates use a small radius for near-duplicate
/// suppression only (avoids false cross-connections between parallel trails).
/// </summary>
public class SpatialGraph
{
    // Segment start/end points: merge within 30 m to bridge typical GPS data gaps.
    private const double EndpointMergeM = 30;
    // Interior shape points: deduplicate only (5 m); never merge across segments.
    private const double InteriorMergeM = 5;

    // Grid cell ~22 m at mid-latitudes. 3×3 neighbourhood covers ±44 m,
    // sufficient for the 30 m endpoint radius.
    private const double CellDeg = 0.0002;

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
        var grid = new Dictionary<(int, int), List<int>>();

        static (int, int) CellOf(double lat, double lon) =>
            ((int)Math.Floor(lat / CellDeg), (int)Math.Floor(lon / CellDeg));

        int GetOrCreateNode(double lat, double lon, double mergeRadius)
        {
            var (cl, cn) = CellOf(lat, lon);
            for (int dl = -1; dl <= 1; dl++)
            {
                for (int dn = -1; dn <= 1; dn++)
                {
                    if (!grid.TryGetValue((cl + dl, cn + dn), out var bucket)) continue;
                    foreach (int id in bucket)
                    {
                        if (RouteUtils.HaversineMeters(lat, lon, nodes[id].Lat, nodes[id].Lon) <= mergeRadius)
                            return id;
                    }
                }
            }
            var node = new GraphNode(nodes.Count, lat, lon);
            nodes.Add(node);
            adj[node.Id] = new List<GraphEdge>();
            var cell = CellOf(lat, lon);
            if (!grid.TryGetValue(cell, out var newBucket))
                grid[cell] = newBucket = new List<int>();
            newBucket.Add(node.Id);
            return node.Id;
        }

        void AddEdge(int from, int to, double distM, string? fid)
        {
            adj[from].Add(new GraphEdge(from, to, distM, fid));
            adj[to].Add(new GraphEdge(to, from, distM, fid));
        }

        foreach (var feature in features)
        {
            var coords = feature.Coordinates;
            if (coords.Count < 2) continue;

            int prevId = GetOrCreateNode(coords[0].Lat, coords[0].Lon, EndpointMergeM);
            for (int i = 1; i < coords.Count; i++)
            {
                double mergeR = (i == coords.Count - 1) ? EndpointMergeM : InteriorMergeM;
                int currId = GetOrCreateNode(coords[i].Lat, coords[i].Lon, mergeR);
                if (currId == prevId) continue;

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
