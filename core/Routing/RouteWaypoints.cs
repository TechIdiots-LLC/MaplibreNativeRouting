using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Routing;

/// <summary>What a point of a route is.</summary>
public enum WaypointKind { Origin, Stop, Destination }

/// <summary>
/// The points a route is planned through, edited as a user plans it: the first is the origin, the last the
/// destination, those between stops on the way. A point added on the route itself goes between the two it falls
/// between; a stop added anywhere else goes where it lengthens the trip least. Feeds <see cref="RouteOptions"/> with
/// <see cref="ApplyTo"/>.
///
/// The model (an ordered list whose ends are the origin and destination, a point put on a route leg inserted after
/// that leg's start, a point tapped to remove it) is ported from maplibre-gl-directions (NOTICE.md).
/// </summary>
public sealed class RouteWaypoints
{
    private readonly List<(double Lat, double Lon)> _points = [];

    public IReadOnlyList<(double Lat, double Lon)> Points => _points;
    public int Count => _points.Count;
    public (double Lat, double Lon)? Origin => _points.Count > 0 ? _points[0] : null;
    public (double Lat, double Lon)? Destination => _points.Count > 1 ? _points[^1] : null;
    public IReadOnlyList<(double Lat, double Lon)> Stops => _points.Count > 2 ? _points.GetRange(1, _points.Count - 2) : [];
    public bool IsComplete => _points.Count >= 2;

    /// <summary>Any change: something to plan again.</summary>
    public event EventHandler? Changed;

    public WaypointKind KindOf(int index) =>
        index == 0 ? WaypointKind.Origin : index == _points.Count - 1 && _points.Count > 1 ? WaypointKind.Destination : WaypointKind.Stop;

    /// <summary>The origin: replaced, or the first point.</summary>
    public void SetOrigin((double Lat, double Lon) point)
    {
        if (_points.Count > 0) _points[0] = point; else _points.Add(point);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The destination: replaced once there is one, added after the origin otherwise.</summary>
    public void SetDestination((double Lat, double Lon) point)
    {
        if (_points.Count >= 2) _points[^1] = point; else _points.Add(point);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A stop on the way, where it lengthens the trip least: between the two points the detour through it is
    /// shortest for, as the crow flies. Without a destination yet, it's the destination.
    /// </summary>
    /// <returns>Its index.</returns>
    public int AddStop((double Lat, double Lon) point)
    {
        if (_points.Count < 2)
        {
            _points.Add(point);
            Changed?.Invoke(this, EventArgs.Empty);
            return _points.Count - 1;
        }
        int best = 1;
        double bestExtra = double.MaxValue;
        for (int i = 1; i < _points.Count; i++)
        {
            double extra = Meters(_points[i - 1], point) + Meters(point, _points[i]) - Meters(_points[i - 1], _points[i]);
            if (extra < bestExtra) (best, bestExtra) = (i, extra);
        }
        _points.Insert(best, point);
        Changed?.Invoke(this, EventArgs.Empty);
        return best;
    }

    /// <summary>
    /// A stop put on the route: between the two points of the leg it falls on (the leg's index, plus one), as
    /// maplibre-gl-directions inserts a point dragged from a route line. Null when the route has no leg near it.
    /// </summary>
    public int? InsertOnRoute((double Lat, double Lon) point, DirectionsRoute route, double maxMeters = 100)
    {
        if (NearestLeg(point, route, maxMeters) is not int leg || leg + 1 > _points.Count - 1) return null;
        _points.Insert(leg + 1, point);
        Changed?.Invoke(this, EventArgs.Empty);
        return leg + 1;
    }

    public void Move(int index, (double Lat, double Lon) point)
    {
        if (index < 0 || index >= _points.Count) return;
        _points[index] = point;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _points.Count) return;
        _points.RemoveAt(index);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _points.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Options with this origin, these stops and this destination.</summary>
    public RouteOptions ApplyTo(RouteOptions options) => IsComplete
        ? options with { Origin = _points[0], Destination = _points[^1], Waypoints = Stops }
        : throw new InvalidOperationException("A route needs an origin and a destination");

    /// <summary>The point within <paramref name="maxMeters"/> of a place, nearest first: for a tap on one.</summary>
    public int? NearestPoint((double Lat, double Lon) place, double maxMeters)
    {
        int? best = null;
        double bestD = maxMeters;
        for (int i = 0; i < _points.Count; i++)
        {
            double d = Meters(place, _points[i]);
            if (d <= bestD) (best, bestD) = (i, d);
        }
        return best;
    }

    /// <summary>The leg of a route nearest a place, if within <paramref name="maxMeters"/> of it.</summary>
    public static int? NearestLeg((double Lat, double Lon) place, DirectionsRoute route, double maxMeters)
    {
        int? best = null;
        double bestD = maxMeters;
        for (int i = 0; i < route.Legs.Count; i++)
        {
            var shape = route.Legs[i].Shape;
            if (shape.Count == 0) continue;
            double d = RouteGeometry.SnapToLine(place.Lat, place.Lon, shape, RouteGeometry.Cumulative(shape)).DistanceMeters;
            if (d <= bestD) (best, bestD) = (i, d);
        }
        return best;
    }

    private static double Meters((double Lat, double Lon) a, (double Lat, double Lon) b) =>
        RouteUtils.HaversineMeters(a.Lat, a.Lon, b.Lat, b.Lon);
}
