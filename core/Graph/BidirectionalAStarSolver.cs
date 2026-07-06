using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Graph;

public static class BidirectionalAStarSolver
{
    public static List<RoadEdge>? FindPath(
        RoadGraph graph, GraphNode start, GraphNode goal,
        double maxSpeedMps, CancellationToken ct = default)
    {
        if (start.Id == goal.Id) return [];

        var openF = new PriorityQueue<int, double>();
        var openB = new PriorityQueue<int, double>();

        var gF = new Dictionary<int, double> { [start.Id] = 0 };
        var gB = new Dictionary<int, double> { [goal.Id] = 0 };

        var cameFromF = new Dictionary<int, (int PrevNode, RoadEdge Edge)>();
        var cameFromB = new Dictionary<int, (int PrevNode, RoadEdge Edge)>();

        double hF(GraphNode n) => RouteUtils.HaversineMeters(n.Lat, n.Lon, goal.Lat, goal.Lon) / maxSpeedMps;
        double hB(GraphNode n) => RouteUtils.HaversineMeters(n.Lat, n.Lon, start.Lat, start.Lon) / maxSpeedMps;

        openF.Enqueue(start.Id, hF(start));
        openB.Enqueue(goal.Id, hB(goal));

        double bestCost = double.MaxValue;
        int meetingNode = -1;

        var nodeMap = new Dictionary<int, GraphNode>();
        foreach (var n in graph.Nodes)
            nodeMap[n.Id] = n;

        double minF = 0, minB = 0;

        while (openF.Count > 0 && openB.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            if (minF + minB >= bestCost)
                break;

            if (minF <= minB)
            {
                if (!openF.TryDequeue(out int cur, out minF)) break;
                if (minF > bestCost) break;

                if (graph.ForwardAdjacency.TryGetValue(cur, out var edges))
                {
                    double curG = gF.GetValueOrDefault(cur, double.MaxValue);
                    foreach (var edge in edges)
                    {
                        double tentative = curG + edge.Cost;
                        if (tentative < gF.GetValueOrDefault(edge.ToNodeId, double.MaxValue))
                        {
                            gF[edge.ToNodeId] = tentative;
                            cameFromF[edge.ToNodeId] = (cur, edge);
                            double f = tentative + hF(nodeMap[edge.ToNodeId]);
                            openF.Enqueue(edge.ToNodeId, f);

                            if (gB.TryGetValue(edge.ToNodeId, out double gb))
                            {
                                double total = tentative + gb;
                                if (total < bestCost)
                                {
                                    bestCost = total;
                                    meetingNode = edge.ToNodeId;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                if (!openB.TryDequeue(out int cur, out minB)) break;
                if (minB > bestCost) break;

                if (graph.BackwardAdjacency.TryGetValue(cur, out var edges))
                {
                    double curG = gB.GetValueOrDefault(cur, double.MaxValue);
                    foreach (var edge in edges)
                    {
                        double tentative = curG + edge.Cost;
                        if (tentative < gB.GetValueOrDefault(edge.FromNodeId, double.MaxValue))
                        {
                            gB[edge.FromNodeId] = tentative;
                            cameFromB[edge.FromNodeId] = (cur, edge);
                            double f = tentative + hB(nodeMap[edge.FromNodeId]);
                            openB.Enqueue(edge.FromNodeId, f);

                            if (gF.TryGetValue(edge.FromNodeId, out double gf))
                            {
                                double total = tentative + gf;
                                if (total < bestCost)
                                {
                                    bestCost = total;
                                    meetingNode = edge.FromNodeId;
                                }
                            }
                        }
                    }
                }
            }
        }

        if (meetingNode < 0) return null;
        return ReconstructPath(cameFromF, cameFromB, start.Id, goal.Id, meetingNode);
    }

    private static List<RoadEdge> ReconstructPath(
        Dictionary<int, (int PrevNode, RoadEdge Edge)> cameFromF,
        Dictionary<int, (int PrevNode, RoadEdge Edge)> cameFromB,
        int startId, int goalId, int meetingNode)
    {
        var forwardEdges = new List<RoadEdge>();
        int cur = meetingNode;
        while (cur != startId && cameFromF.TryGetValue(cur, out var prev))
        {
            forwardEdges.Add(prev.Edge);
            cur = prev.PrevNode;
        }
        forwardEdges.Reverse();

        var backwardEdges = new List<RoadEdge>();
        cur = meetingNode;
        while (cur != goalId && cameFromB.TryGetValue(cur, out var prev))
        {
            backwardEdges.Add(prev.Edge);
            cur = prev.PrevNode;
        }

        forwardEdges.AddRange(backwardEdges);
        return forwardEdges;
    }
}
