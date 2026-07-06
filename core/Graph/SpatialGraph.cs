using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Graph;

/// <summary>
/// Builds an undirected spatial graph from TrackFeature (LineString) coordinate arrays.
///
/// Two-phase construction (inspired by geojson-path-finder + turf's addMissingIntersectionPoints):
///   Phase 1 — T-junction insertion: for each trail segment endpoint, project it onto
///              every nearby trail segment's interior. If the perpendicular distance is
///              within JunctionSnapM, insert the projected point into that segment so the
///              graph has an explicit node at the T-intersection.
///   Phase 2 — Graph build: create nodes with differentiated merge radii (endpoints 15 m,
///              interior 5 m) and connect them with edges.
/// </summary>
public class SpatialGraph
{
    // After junction insertion endpoints of different features are already snapped together,
    // so a small merge radius suffices for de-duplication.
    private const double EndpointMergeM = 15;
    private const double InteriorMergeM = 5;

    // How far a trail endpoint can be from another trail's line to still insert a junction node.
    private const double JunctionSnapM = 25;

    // Grid cell ~22 m at mid-latitudes; 3×3 neighbourhood covers ±44 m.
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
        // ── Phase 1: T-junction insertion ───────────────────────────────────────────
        // Materialise mutable coordinate lists (format: (Lon, Lat) matching TrackFeature).
        var coordLists = features
            .Where(f => f.Coordinates.Count >= 2)
            .Select(f => f.Coordinates.ToList())
            .ToList();

        InsertMissingJunctions(coordLists, JunctionSnapM);

        // ── Phase 2: Graph build ────────────────────────────────────────────────────
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

        // coordLists may have more entries inserted (junction points); keep original
        // feature IDs aligned by index where possible.
        for (int fi = 0; fi < coordLists.Count; fi++)
        {
            var coords = coordLists[fi];
            if (coords.Count < 2) continue;
            string? fid = fi < features.Count ? features[fi].FeatureId : null;

            int prevId = GetOrCreateNode(coords[0].Lat, coords[0].Lon, EndpointMergeM);
            for (int i = 1; i < coords.Count; i++)
            {
                double mergeR = (i == coords.Count - 1) ? EndpointMergeM : InteriorMergeM;
                int currId = GetOrCreateNode(coords[i].Lat, coords[i].Lon, mergeR);
                if (currId == prevId) continue;

                double distM = RouteUtils.HaversineMeters(
                    coords[i - 1].Lat, coords[i - 1].Lon,
                    coords[i].Lat, coords[i].Lon);
                AddEdge(prevId, currId, distM, fid);
                prevId = currId;
            }
        }

        return new SpatialGraph(nodes, adj);
    }

    /// <summary>Returns the node closest to the given coordinate, or null if empty.</summary>
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

    // ── T-junction insertion ────────────────────────────────────────────────────────

    /// <summary>
    /// For each trail segment's start and end point, finds the nearest point on every
    /// other segment's interior. If that perpendicular distance is within
    /// <paramref name="thresholdM"/>, inserts a new coordinate at the projected position so
    /// the graph builder creates an explicit node there, connecting the two trails.
    /// Uses a bounding-box pre-filter so only nearby segments are checked.
    /// </summary>
    private static void InsertMissingJunctions(
        List<List<(double Lon, double Lat)>> coordLists,
        double thresholdM)
    {
        // 1° ≈ 111 km; convert threshold to a degree margin for bbox pre-filter.
        double threshDeg = thresholdM / 90_000.0;

        // Pre-compute expanded bboxes for all features.
        var bboxes = new (double MinLat, double MaxLat, double MinLon, double MaxLon)[coordLists.Count];
        for (int i = 0; i < coordLists.Count; i++)
        {
            double minLat = double.MaxValue, maxLat = double.MinValue;
            double minLon = double.MaxValue, maxLon = double.MinValue;
            foreach (var c in coordLists[i])
            {
                if (c.Lat < minLat) minLat = c.Lat;
                if (c.Lat > maxLat) maxLat = c.Lat;
                if (c.Lon < minLon) minLon = c.Lon;
                if (c.Lon > maxLon) maxLon = c.Lon;
            }
            bboxes[i] = (minLat - threshDeg, maxLat + threshDeg,
                         minLon - threshDeg, maxLon + threshDeg);
        }

        for (int i = 0; i < coordLists.Count; i++)
        {
            var coords = coordLists[i];
            if (coords.Count < 2) continue;

            // Only check endpoints; interior points are handled by the merge radius.
            foreach (var ep in new[] { coords[0], coords[^1] })
            {
                double eLat = ep.Lat, eLon = ep.Lon;

                for (int j = 0; j < coordLists.Count; j++)
                {
                    if (i == j) continue;

                    // Bbox pre-filter.
                    var bb = bboxes[j];
                    if (eLat < bb.MinLat || eLat > bb.MaxLat) continue;
                    if (eLon < bb.MinLon || eLon > bb.MaxLon) continue;

                    TryInsertProjection(eLat, eLon, coordLists[j], thresholdM);
                }
            }
        }
    }

    /// <summary>
    /// Projects (eLat, eLon) onto each segment of <paramref name="target"/>.
    /// Inserts the best projected point if it is within <paramref name="thresholdM"/>
    /// and not already within 2 m of an existing vertex.
    /// </summary>
    private static void TryInsertProjection(
        double eLat, double eLon,
        List<(double Lon, double Lat)> target,
        double thresholdM)
    {
        double bestDistM = thresholdM;
        int bestIdx = -1;
        double bestLat = 0, bestLon = 0;

        for (int k = 0; k < target.Count - 1; k++)
        {
            var (projLat, projLon) = ProjectOnSegment(
                eLat, eLon,
                target[k].Lat, target[k].Lon,
                target[k + 1].Lat, target[k + 1].Lon);

            double d = RouteUtils.HaversineMeters(eLat, eLon, projLat, projLon);
            if (d < bestDistM)
            {
                bestDistM = d;
                bestIdx = k;
                bestLat = projLat;
                bestLon = projLon;
            }
        }

        if (bestIdx < 0) return;

        // Skip if already within 2 m of an existing vertex (no duplicate needed).
        if (RouteUtils.HaversineMeters(target[bestIdx].Lat, target[bestIdx].Lon, bestLat, bestLon) < 2.0) return;
        if (RouteUtils.HaversineMeters(target[bestIdx + 1].Lat, target[bestIdx + 1].Lon, bestLat, bestLon) < 2.0) return;

        target.Insert(bestIdx + 1, (bestLon, bestLat));
    }

    /// <summary>
    /// Projects point P onto segment A→B using a local Cartesian approximation
    /// (cos(lat) correction for longitude). Returns the clamped projection (t ∈ [0,1]).
    /// </summary>
    private static (double Lat, double Lon) ProjectOnSegment(
        double pLat, double pLon,
        double aLat, double aLon,
        double bLat, double bLon)
    {
        double cosLat = Math.Cos((aLat + bLat + pLat) / 3.0 * Math.PI / 180.0);
        double dx = (bLon - aLon) * cosLat;
        double dy = bLat - aLat;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-20) return (aLat, aLon);
        double px = (pLon - aLon) * cosLat;
        double py = pLat - aLat;
        double t = Math.Clamp((px * dx + py * dy) / lenSq, 0.0, 1.0);
        return (aLat + t * (bLat - aLat), aLon + t * (bLon - aLon));
    }
}
