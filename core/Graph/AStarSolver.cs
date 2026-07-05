using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Graph;

/// <summary>Generic A* path-finder over a SpatialGraph. Heuristic: haversine distance
/// to the goal node. Returns null if no path exists.</summary>
public static class AStarSolver
{
    public static List<GraphNode>? FindPath(SpatialGraph graph, GraphNode start, GraphNode goal)
    {
        if (start.Id == goal.Id) return [start];

        var openSet = new PriorityQueue<int, double>();
        var gScore = new Dictionary<int, double> { [start.Id] = 0 };
        var fScore = new Dictionary<int, double>();
        var cameFrom = new Dictionary<int, int>();

        double h(GraphNode n) => RouteUtils.HaversineMeters(n.Lat, n.Lon, goal.Lat, goal.Lon);

        fScore[start.Id] = h(start);
        openSet.Enqueue(start.Id, fScore[start.Id]);

        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);

        while (openSet.Count > 0)
        {
            var currentId = openSet.Dequeue();
            if (currentId == goal.Id)
                return ReconstructPath(cameFrom, nodeMap, currentId);

            if (!graph.Adjacency.TryGetValue(currentId, out var edges)) continue;

            foreach (var edge in edges)
            {
                var tentative = gScore.GetValueOrDefault(currentId, double.MaxValue) + edge.DistanceMeters;
                if (tentative < gScore.GetValueOrDefault(edge.ToNodeId, double.MaxValue))
                {
                    cameFrom[edge.ToNodeId] = currentId;
                    gScore[edge.ToNodeId] = tentative;
                    fScore[edge.ToNodeId] = tentative + h(nodeMap[edge.ToNodeId]);
                    openSet.Enqueue(edge.ToNodeId, fScore[edge.ToNodeId]);
                }
            }
        }

        return null; // no path
    }

    private static List<GraphNode> ReconstructPath(
        Dictionary<int, int> cameFrom,
        Dictionary<int, GraphNode> nodeMap,
        int current)
    {
        var path = new List<GraphNode> { nodeMap[current] };
        while (cameFrom.TryGetValue(current, out var prev))
        {
            path.Add(nodeMap[prev]);
            current = prev;
        }
        path.Reverse();
        return path;
    }
}
