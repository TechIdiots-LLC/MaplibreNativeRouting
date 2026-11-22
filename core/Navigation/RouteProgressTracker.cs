using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Navigation;

/// <summary>
/// Tracks the user's position along an active route. On each location fix, it:
///   1. Snaps the position to the nearest point of the route's line, looking ahead of where it last was, so a route
///      that doubles back on itself isn't jumped along.
///   2. Moves on to the step the snapped point lies in (never back: going the wrong way is the off-route
///      detector's to notice).
///   3. Computes the distance left in the step and the route, the fraction travelled, and how far the user is from
///      the line.
///   4. Reports arrival within <see cref="ArrivalMeters"/> of the end.
///
/// Ported from maplibre-navigation-android's NavigationRouteProcessor / RouteProgress logic (NOTICE.md).
/// </summary>
public class RouteProgressTracker
{
    /// <summary>The end is reached this near it (maplibre-navigation-android's METERS_REMAINING_TILL_ARRIVAL).</summary>
    public const double ArrivalMeters = 40;

    private const int BackWindow = 5;        // segments behind the last snap still searched
    private const int AheadWindow = 400;     // and ahead of it
    private const double LostMeters = 50;    // nearer than this in the window, and the whole leg isn't searched

    private DirectionsRoute _route;
    private double[][] _cumulative;
    private int _legIndex;
    private int _stepIndex;
    private int _segmentHint;

    public RouteProgressTracker(DirectionsRoute route)
    {
        _route = route;
        _cumulative = route.Legs.Select(l => RouteGeometry.Cumulative(l.Shape)).ToArray();
    }

    /// <summary>Updates with a new GPS fix and returns the current RouteProgress.</summary>
    public RouteProgress Update(double lat, double lon) => Update(new LocationFix(lat, lon));

    /// <summary>Updates with a new location fix and returns the current RouteProgress.</summary>
    public RouteProgress Update(LocationFix fix)
    {
        var leg = _route.Legs[_legIndex];
        var cum = _cumulative[_legIndex];

        // Ahead of where it was first; the whole leg only when that's nowhere near
        var snap = RouteGeometry.SnapToLine(fix.Lat, fix.Lon, leg.Shape, cum,
            _segmentHint - BackWindow, _segmentHint + AheadWindow);
        if (snap.DistanceMeters > LostMeters)
        {
            var anywhere = RouteGeometry.SnapToLine(fix.Lat, fix.Lon, leg.Shape, cum);
            if (anywhere.DistanceMeters < snap.DistanceMeters) snap = anywhere;
        }
        _segmentHint = snap.Segment;

        // Into the next leg once this one's end is reached
        if (_legIndex < _route.Legs.Count - 1 && cum.Length > 0 && cum[^1] - snap.AlongMeters <= ArrivalMeters)
        {
            _legIndex++;
            _stepIndex = 0;
            _segmentHint = 0;
            return Update(fix);
        }

        // The step the point lies in: steps run from their BeginShapeIndex, and are only ever moved on from
        while (_stepIndex < leg.Steps.Count - 1)
        {
            var next = leg.Steps[_stepIndex + 1];
            bool isArrival = next.BeginShapeIndex >= leg.Shape.Count - 1;
            if (isArrival || snap.Segment < next.BeginShapeIndex) break;
            _stepIndex++;
        }
        var step = leg.Steps[_stepIndex];

        double legLength = cum.Length > 0 ? cum[^1] : 0;
        double stepEnd = cum.Length > 0 ? cum[Math.Clamp(step.EndShapeIndex, 0, cum.Length - 1)] : 0;
        double stepRemaining = Math.Max(0, stepEnd - snap.AlongMeters);
        double remaining = Math.Max(0, legLength - snap.AlongMeters);
        for (int li = _legIndex + 1; li < _route.Legs.Count; li++)
            remaining += _route.Legs[li].Distance;
        double total = _route.Distance > 0 ? _route.Distance : Math.Max(1, legLength);

        return new RouteProgress
        {
            Route = _route,
            CurrentLegIndex = _legIndex,
            CurrentStepIndex = _stepIndex,
            DistanceRemainingMeters = remaining,
            DurationRemainingSeconds = EstimateDuration(remaining),
            StepDistanceRemainingMeters = stepRemaining,
            FractionTraveled = Math.Clamp((total - remaining) / total, 0, 1),
            SnappedLocation = (snap.Lon, snap.Lat),
            Location = fix,
            DistanceFromRouteMeters = snap.DistanceMeters,
            HasArrived = _legIndex == _route.Legs.Count - 1 && remaining <= ArrivalMeters,
        };
    }

    public void SetRoute(DirectionsRoute newRoute)
    {
        _route = newRoute;
        _cumulative = newRoute.Legs.Select(l => RouteGeometry.Cumulative(l.Shape)).ToArray();
        _legIndex = 0;
        _stepIndex = 0;
        _segmentHint = 0;
    }

    private double EstimateDuration(double remainingMeters)
    {
        // Rough estimate: use the route's average speed.
        if (_route.Distance <= 0 || _route.Duration <= 0) return 0;
        double metersPerSecond = _route.Distance / _route.Duration;
        return remainingMeters / metersPerSecond;
    }
}
