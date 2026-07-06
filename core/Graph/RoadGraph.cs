using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Routing;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Graph;

public sealed class RoadGraph
{
    private const long CoordScale = 10_000_000L;
    private const double CellSizeDeg = 0.001; // ~111m at equator

    public IReadOnlyList<GraphNode> Nodes => _nodes;
    public IReadOnlyDictionary<int, List<RoadEdge>> ForwardAdjacency => _forwardAdj;
    public IReadOnlyDictionary<int, List<RoadEdge>> BackwardAdjacency => _backwardAdj;

    private readonly List<GraphNode> _nodes;
    private readonly Dictionary<int, List<RoadEdge>> _forwardAdj;
    private readonly Dictionary<int, List<RoadEdge>> _backwardAdj;
    private readonly Dictionary<(int CellX, int CellY), List<int>> _spatialHash;

    private RoadGraph(
        List<GraphNode> nodes,
        Dictionary<int, List<RoadEdge>> fwd,
        Dictionary<int, List<RoadEdge>> bwd,
        Dictionary<(int, int), List<int>> hash)
    {
        _nodes = nodes;
        _forwardAdj = fwd;
        _backwardAdj = bwd;
        _spatialHash = hash;
    }

    internal static RoadGraph Build(IReadOnlyList<MvtRoadSegment> segments, MvtCostingModel costing)
    {
        var nodes = new List<GraphNode>();
        var coordToId = new Dictionary<(long, long), int>();
        var fwd = new Dictionary<int, List<RoadEdge>>();
        var bwd = new Dictionary<int, List<RoadEdge>>();
        var spatialHash = new Dictionary<(int, int), List<int>>();

        int GetOrCreateNode(double lat, double lon)
        {
            var key = ((long)(lat * CoordScale), (long)(lon * CoordScale));
            if (coordToId.TryGetValue(key, out int existingId))
                return existingId;

            int id = nodes.Count;
            double snappedLat = key.Item1 / (double)CoordScale;
            double snappedLon = key.Item2 / (double)CoordScale;
            nodes.Add(new GraphNode(id, snappedLat, snappedLon));
            coordToId[key] = id;
            fwd[id] = [];
            bwd[id] = [];

            var cell = ((int)Math.Floor(snappedLon / CellSizeDeg), (int)Math.Floor(snappedLat / CellSizeDeg));
            if (!spatialHash.TryGetValue(cell, out var list))
            {
                list = [];
                spatialHash[cell] = list;
            }
            list.Add(id);

            return id;
        }

        foreach (var seg in segments)
        {
            if (!costing.IsTraversable(seg.RoadClass, seg.Subclass, seg.AccessRestricted))
                continue;

            var coords = seg.Coordinates;
            if (coords.Count < 2) continue;

            double speed = costing.GetSpeed(seg.RoadClass, seg.Subclass);
            if (speed <= 0) continue;

            bool isBridge = seg.Brunnel is "bridge";
            bool isTunnel = seg.Brunnel is "tunnel";

            for (int i = 0; i < coords.Count - 1; i++)
            {
                int fromId = GetOrCreateNode(coords[i].Lat, coords[i].Lon);
                int toId = GetOrCreateNode(coords[i + 1].Lat, coords[i + 1].Lon);
                if (fromId == toId) continue;

                double dist = RouteUtils.HaversineMeters(
                    coords[i].Lat, coords[i].Lon,
                    coords[i + 1].Lat, coords[i + 1].Lon);

                double cost = costing.ComputeCost(dist, seg.RoadClass, seg.Subclass, seg.Surface, seg.Toll, seg.Ramp);

                RoadEdge MakeEdge(int from, int to) => new(
                    from, to, dist,
                    seg.RoadClass, seg.Subclass, seg.Surface,
                    seg.Name, seg.Ref,
                    seg.Toll, seg.Ramp, isBridge, isTunnel,
                    speed, []);

                if (seg.Oneway != -1) // forward allowed
                {
                    var edge = MakeEdge(fromId, toId);
                    edge.Cost = cost;
                    fwd[fromId].Add(edge);
                    bwd[toId].Add(edge);
                }

                if (seg.Oneway != 1) // backward allowed
                {
                    var edge = MakeEdge(toId, fromId);
                    edge.Cost = cost;
                    fwd[toId].Add(edge);
                    bwd[fromId].Add(edge);
                }
            }
        }

        return new RoadGraph(nodes, fwd, bwd, spatialHash);
    }

    public GraphNode? NearestNode(double lat, double lon)
    {
        int cx = (int)Math.Floor(lon / CellSizeDeg);
        int cy = (int)Math.Floor(lat / CellSizeDeg);

        GraphNode? best = null;
        double bestDist = double.MaxValue;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (!_spatialHash.TryGetValue((cx + dx, cy + dy), out var ids))
                    continue;
                foreach (var id in ids)
                {
                    var n = _nodes[id];
                    var d = RouteUtils.HaversineMeters(lat, lon, n.Lat, n.Lon);
                    if (d < bestDist) { bestDist = d; best = n; }
                }
            }
        }

        if (best != null) return best;

        // fallback: wider search if no nodes in adjacent cells
        foreach (var n in _nodes)
        {
            var d = RouteUtils.HaversineMeters(lat, lon, n.Lat, n.Lon);
            if (d < bestDist) { bestDist = d; best = n; }
        }
        return best;
    }
}
