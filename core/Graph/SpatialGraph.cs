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

    // Cell size ~22 m at mid-latitudes. A 3×3 neighbourhood covers ±44 m,
    // enough to catch all merge candidates without a full linear scan.
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
        // Spatial hash: grid-cell → list of node IDs in that cell.
        var grid = new Dictionary<(int, int), List<int>>();

        static (int, int) CellOf(double lat, double lon) =>
            ((int)Math.Floor(lat / CellDeg), (int)Math.Floor(lon / CellDeg));

        int GetOrCreateNode(double lat, double lon)
        {
            var (cl, cn) = CellOf(lat, lon);
            // Search 3×3 neighbourhood of cells — O(1) instead of O(N).
            for (int dl = -1; dl <= 1; dl++)
            {
                for (int dn = -1; dn <= 1; dn++)
                {
                    if (!grid.TryGetValue((cl + dl, cn + dn), out var bucket)) continue;
                    foreach (int id in bucket)
                    {
                        if (RouteUtils.HaversineMeters(lat, lon, nodes[id].Lat, nodes[id].Lon) <= MergeRadiusM)
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
