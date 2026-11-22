using MaplibreNative.Routing.Core.Mvt;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Routing;

/// <summary>How closely a road's name matched what was asked for.</summary>
public enum RoadMatchLevel
{
    /// <summary>Its name or number, however written: "Elm St", "Rt 9".</summary>
    Exact,
    /// <summary>Its name without its kind or direction: "Elm" for Elm Street.</summary>
    Base,
    /// <summary>A name a letter or two from it: "Pheasant Street" for Pleasant Street.</summary>
    Fuzzy,
}

/// <summary>A road, or a corner of two, found by name.</summary>
public sealed record RoadMatch
{
    /// <summary>The road's name ("Main Street"), with its number when found by it ("MA 31 (Paxton Road)"); for a
    /// corner, both ("Main Street and MA 9").</summary>
    public required string Name { get; init; }

    /// <summary>The road's number, as its network writes it ("MA 31"), when it has one.</summary>
    public string? Ref { get; init; }

    /// <summary>A corner where two roads asked for meet ("Main St and Route 9"), not one road.</summary>
    public bool IsCorner { get; init; }

    public RoadMatchLevel Level { get; init; }

    /// <summary>Where to go: the road's point nearest the search's centre, or the corner.</summary>
    public required (double Lat, double Lon) Location { get; init; }

    /// <summary>From the search's centre to <see cref="Location"/>.</summary>
    public double DistanceMeters { get; init; }

    /// <summary>The road's lines, [lon, lat], to draw it; the two roads' for a corner.</summary>
    public IReadOnlyList<IReadOnlyList<(double Lon, double Lat)>> Lines { get; init; } = [];
}

/// <summary>
/// Finds roads by name near a place, from the same OpenMapTiles tiles the offline router uses
/// (<c>transportation_name</c> at zoom 14): "Elm St", "Route 9", "Interstate 190", "Main St and Route 9" (a corner),
/// or a misheard "Pheasant Street" for Pleasant Street. Names are read as <see cref="RoadNames"/> reads them. A road
/// is the stretches of its name that join each other, so Main Street here and Main Street in the next town are two.
/// </summary>
public sealed class RoadSearch
{
    private const int Zoom = 14;
    private const double MaxRadiusKm = 25;
    private const double JoinM = 40;     // stretches of a name whose ends come this near each other are one road
    private const double CornerM = 30;   // two roads this near each other meet

    private readonly string _source;
    private readonly ITileCacheProvider? _cache;
    private TileProvider? _tiles;

    /// <param name="tileJsonUrl">An OpenMapTiles TileJSON, or a tile URL template with {z}/{x}/{y}.</param>
    /// <param name="cacheProvider">The app's tile cache, shared as the router shares it.</param>
    public RoadSearch(string tileJsonUrl, ITileCacheProvider? cacheProvider = null)
    {
        _source = tileJsonUrl;
        _cache = cacheProvider;
    }

    private sealed record Piece(string? Name, string? Ref, IReadOnlyList<string> Keys, List<(double Lon, double Lat)> Line);

    /// <summary>
    /// Roads, or corners, by name within <paramref name="radiusKm"/> of a place: corners first for an address naming
    /// two roads; then the closest matches, nearest first.
    /// </summary>
    public async Task<IReadOnlyList<RoadMatch>> FindAsync(string query, double lat, double lon,
        double radiusKm = 10, int maxResults = 5, CancellationToken ct = default)
    {
        var parts = RoadNames.AddressRoads(query);
        if (parts.Count == 0) return [];
        var pieces = await LoadAsync(lat, lon, Math.Clamp(radiusKm, 0.5, MaxRadiusKm), ct).ConfigureAwait(false);
        var geo = new Geo(lat);

        var found = parts.Take(2).Select(p => Locate(p, pieces, geo, lat, lon)).ToList();
        var results = new List<RoadMatch>();
        if (found.Count == 2)
        {
            foreach (var a in found[0])
                foreach (var b in found[1])
                {
                    if (geo.Corner(a.Lines, b.Lines, CornerM) is not { } at) continue;
                    results.Add(new RoadMatch
                    {
                        Name = $"{a.Name} and {b.Name}", IsCorner = true,
                        Level = (RoadMatchLevel)Math.Max((int)a.Level, (int)b.Level),
                        Location = (at.Lat, at.Lon), DistanceMeters = geo.Meters((lat, lon), at),
                        Lines = [.. a.Lines, .. b.Lines],
                    });
                }
        }
        results.Sort((x, y) => x.Level.CompareTo(y.Level) != 0 ? x.Level.CompareTo(y.Level) : x.DistanceMeters.CompareTo(y.DistanceMeters));
        // Each road asked for, after any corner
        results.AddRange(found.SelectMany(f => f).OrderBy(m => m.Level).ThenBy(m => m.DistanceMeters));
        return results.Take(maxResults).ToList();
    }

    private async Task<List<Piece>> LoadAsync(double lat, double lon, double radiusKm, CancellationToken ct)
    {
        _tiles ??= new TileProvider(_source.Contains("{z}") ? _source
            : await TileProvider.ResolveTileJsonAsync(_source, ct).ConfigureAwait(false), cacheProvider: _cache);
        double dLat = radiusKm / 110.574, dLon = radiusKm / (111.320 * Math.Max(0.05, Math.Cos(lat * Math.PI / 180)));
        var coords = TileCoord.CoverBoundingBox(lat - dLat, lon - dLon, lat + dLat, lon + dLon, Zoom);
        using var gate = new SemaphoreSlim(6);
        var tiles = await Task.WhenAll(coords.Select(async c =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try { return (Coord: c, Data: await _tiles.GetTileDataAsync(c, ct).ConfigureAwait(false)); }
            finally { gate.Release(); }
        })).ConfigureAwait(false);

        var pieces = new List<Piece>();
        foreach (var (coord, data) in tiles)
        {
            if (data is not { Length: > 0 }) continue;
            var layer = MvtDecoder.Decode(data).GetLayer("transportation_name");
            if (layer == null) continue;
            foreach (var f in layer.Features)
            {
                if (f.Type != MvtGeomType.LineString) continue;
                var props = MvtDecoder.GetProperties(f, layer);
                if (props.TryGetValue("class", out var cls) && cls?.ToString() == "path") continue;   // not a road
                string? name = props.TryGetValue("name", out var n) && n?.ToString() is { Length: > 0 } ns ? ns : null;
                string? @ref = RoadNames.FormatRef(props);
                var keys = RoadNames.Keys(name, @ref);
                if (keys.Count == 0) continue;
                foreach (var ring in f.Geometry)
                {
                    if (ring.Count < 2) continue;
                    var line = ring.Select(p => coord.FeatureToLatLon(p.X, p.Y, layer.Extent)).Select(p => (p.Lon, p.Lat)).ToList();
                    pieces.Add(new Piece(name, @ref, keys, line));
                }
            }
        }
        return pieces;
    }

    /// <summary>The roads a phrase names, each the joined stretches of its name, nearest first.</summary>
    private static List<RoadMatch> Locate(string phrase, List<Piece> pieces, Geo geo, double lat, double lon)
    {
        string key = phrase;
        string? @base = RoadNames.Base(key);
        var level = RoadMatchLevel.Exact;
        var hits = pieces.Where(p => p.Keys.Contains(key)).ToList();
        if (hits.Count == 0 && @base != null)
        {
            level = RoadMatchLevel.Base;
            hits = pieces.Where(p => p.Keys.Any(k => RoadNames.Base(k) == @base)).ToList();
        }
        if (hits.Count == 0 && @base != null && RoadNames.Slack(@base) > 0)
        {
            // Misheard: the nearest name a letter or two from it
            int slack = RoadNames.Slack(@base);
            var near = pieces.SelectMany(p => p.Keys.Select(k => (p, b: RoadNames.Base(k))))
                .Where(x => x.b != null)
                .Select(x => (x.p, x.b, d: RoadNames.Distance(@base, x.b!, slack)))
                .Where(x => x.d <= slack).ToList();
            if (near.Count > 0)
            {
                int best = near.Min(x => x.d);
                level = RoadMatchLevel.Fuzzy;
                hits = near.Where(x => x.d == best).Select(x => x.p).Distinct().ToList();
            }
        }

        // One road per name (or number, when asked for by it: MA 31 is one road whatever each town calls it) and the
        // stretches of it that join; a tile's edge cuts them apart, a little overlapping
        bool ByNumber(Piece p) => p.Ref != null && RoadNames.RefNames(p.Ref).Contains(key);
        var roads = new List<RoadMatch>();
        foreach (var named in hits.GroupBy(p => ByNumber(p) ? p.Ref : p.Name ?? p.Ref))
        {
            var group = named.ToList();
            var parent = Enumerable.Range(0, group.Count).ToArray();
            int Root(int i) => parent[i] == i ? i : parent[i] = Root(parent[i]);
            for (int i = 0; i < group.Count; i++)
                for (int j = 0; j < group.Count; j++)
                    if (i != j && Root(i) != Root(j) && geo.EndsOn(group[i].Line, group[j].Line, JoinM))
                        parent[Root(i)] = Root(j);
            foreach (var road in group.Select((p, i) => (p, i)).GroupBy(x => Root(x.i), x => x.p))
            {
                var lines = road.Select(p => p.Line).ToList();
                var (at, d) = geo.Nearest((lat, lon), lines);
                // Named by its stretch nearest the search's centre
                var first = road.OrderBy(p => geo.Nearest((lat, lon), [p.Line]).Meters).First();
                bool byNumber = ByNumber(first);
                roads.Add(new RoadMatch
                {
                    Name = byNumber && first.Name != null ? $"{first.Ref} ({first.Name})" : first.Name ?? first.Ref!,
                    Ref = first.Ref, Level = level, Location = (at.Lat, at.Lon), DistanceMeters = d,
                    Lines = lines.Select(l => (IReadOnlyList<(double Lon, double Lat)>)l).ToList(),
                });
            }
        }
        return roads.OrderBy(r => r.DistanceMeters).ToList();
    }

    /// <summary>Distances on a flat sheet scaled to the latitude: near enough over a search's few kilometres.</summary>
    private sealed class Geo(double lat)
    {
        private readonly double _kx = Math.Cos(lat * Math.PI / 180) * 111_320, _ky = 110_574;

        public double Meters((double Lat, double Lon) a, (double Lat, double Lon) b) =>
            Math.Sqrt(Math.Pow((a.Lon - b.Lon) * _kx, 2) + Math.Pow((a.Lat - b.Lat) * _ky, 2));

        /// <summary>The point of a segment nearest a place.</summary>
        public (double Lat, double Lon) Project((double Lat, double Lon) p, (double Lon, double Lat) a, (double Lon, double Lat) b)
        {
            double dx = (b.Lon - a.Lon) * _kx, dy = (b.Lat - a.Lat) * _ky, len = dx * dx + dy * dy;
            double t = len == 0 ? 0 : Math.Clamp(((p.Lon - a.Lon) * _kx * dx + (p.Lat - a.Lat) * _ky * dy) / len, 0, 1);
            return (a.Lat + (b.Lat - a.Lat) * t, a.Lon + (b.Lon - a.Lon) * t);
        }

        public ((double Lat, double Lon) At, double Meters) Nearest((double Lat, double Lon) p, IEnumerable<IReadOnlyList<(double Lon, double Lat)>> lines)
        {
            ((double Lat, double Lon) At, double Meters) best = (p, double.MaxValue);
            foreach (var l in lines)
                for (int i = 0; i + 1 < l.Count; i++)
                {
                    var q = Project(p, l[i], l[i + 1]);
                    double d = Meters(p, q);
                    if (d < best.Meters) best = (q, d);
                }
            return best;
        }

        /// <summary>Whether either end of one line lies within <paramref name="m"/> metres of the other.</summary>
        public bool EndsOn(IReadOnlyList<(double Lon, double Lat)> a, IReadOnlyList<(double Lon, double Lat)> b, double m) =>
            new[] { a[0], a[^1] }.Any(e => Nearest((e.Lat, e.Lon), [b]).Meters <= m);

        /// <summary>Where two roads meet: the nearest approach of their lines, if they come within <paramref name="m"/>.</summary>
        public (double Lat, double Lon)? Corner(IReadOnlyList<IReadOnlyList<(double Lon, double Lat)>> a,
            IReadOnlyList<IReadOnlyList<(double Lon, double Lat)>> b, double m)
        {
            // Where they cross: two straight roads may have no vertex near it
            foreach (var la in a)
                for (int i = 0; i + 1 < la.Count; i++)
                    foreach (var lb in b)
                        for (int j = 0; j + 1 < lb.Count; j++)
                            if (Cross(la[i], la[i + 1], lb[j], lb[j + 1]) is { } x) return x;
            ((double Lat, double Lon) At, double Meters) best = (default, double.MaxValue);
            foreach (var l in a)
                foreach (var v in l)
                {
                    var n = Nearest((v.Lat, v.Lon), b);
                    if (n.Meters < best.Meters) best = ((v.Lat, v.Lon), n.Meters);
                }
            return best.Meters <= m ? best.At : null;
        }

        private (double Lat, double Lon)? Cross((double Lon, double Lat) p1, (double Lon, double Lat) p2,
            (double Lon, double Lat) q1, (double Lon, double Lat) q2)
        {
            double rx = (p2.Lon - p1.Lon) * _kx, ry = (p2.Lat - p1.Lat) * _ky;
            double sx = (q2.Lon - q1.Lon) * _kx, sy = (q2.Lat - q1.Lat) * _ky;
            double den = rx * sy - ry * sx;
            if (Math.Abs(den) < 1e-9) return null;
            double qx = (q1.Lon - p1.Lon) * _kx, qy = (q1.Lat - p1.Lat) * _ky;
            double t = (qx * sy - qy * sx) / den, u = (qx * ry - qy * rx) / den;
            return t is >= 0 and <= 1 && u is >= 0 and <= 1
                ? (p1.Lat + (p2.Lat - p1.Lat) * t, p1.Lon + (p2.Lon - p1.Lon) * t) : null;
        }
    }
}
