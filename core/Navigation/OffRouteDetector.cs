using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Navigation;

/// <summary>
/// Decides when the user has left the route. Ported from maplibre-navigation-android's OffRouteDetector and
/// ToleranceUtils (NOTICE.md):
///   - Off the route: further from the current step's line than the off-route radius (the larger of
///     <see cref="NavigationOptions.OffRouteThresholdRadiusMeters"/>, halved within the maneuver zone of the next
///     turn, and the fix's accuracy), unless within the next step's maneuver zone (a turn taken a little early).
///   - The wrong way: on the route but getting further from the next turn over three fixes, by more than
///     <see cref="NavigationOptions.OffRouteMinimumDistanceMetersBeforeWrongDirection"/>.
///   - Neither until the user has moved <see cref="NavigationOptions.OffRouteMinimumDistanceMetersAfterReroute"/>
///     from where navigation started or last rerouted.
/// Android halves the radius near any intersection a route service lists; routes here list none, so it's halved
/// near the next turn.
/// </summary>
public sealed class OffRouteDetector
{
    private const int WrongWayFixes = 3;

    private readonly NavigationOptions _options;
    private (double Lat, double Lon)? _lastReroutePoint;
    private readonly List<double> _awayFromManeuver = new(WrongWayFixes);

    public OffRouteDetector(NavigationOptions? options = null) => _options = options ?? new NavigationOptions();

    /// <summary>Start over from a place: a reroute's start, or none to wait for the next fix.</summary>
    public void Reset((double Lat, double Lon)? from = null)
    {
        _lastReroutePoint = from;
        _awayFromManeuver.Clear();
    }

    public bool IsOffRoute(LocationFix fix, RouteProgress progress)
    {
        if (progress.HasArrived) return false;
        if (!MovedSinceReroute(fix)) return false;

        var leg = progress.CurrentLeg;
        var step = progress.CurrentStep;
        double fromStep = DistanceFromStep(fix, leg, step);
        double radius = Math.Max(DynamicRadius(progress), (fix.AccuracyMeters ?? 0) * _options.DeadReckoningTimeInterval);

        if (fromStep <= radius)
        {
            if (!MovingAwayFromManeuver(progress)) return false;
            _lastReroutePoint = (fix.Lat, fix.Lon);
            return true;
        }

        // Taken the next turn a little early: the tracker moves on to it
        if (progress.UpcomingStep is { } upcoming &&
            DistanceFromStep(fix, leg, upcoming) < _options.ManeuverZoneRadiusMeters)
            return false;

        _lastReroutePoint = (fix.Lat, fix.Lon);
        return true;
    }

    private bool MovedSinceReroute(LocationFix fix)
    {
        if (_lastReroutePoint is not { } last)
        {
            _lastReroutePoint = (fix.Lat, fix.Lon);
            return false;
        }
        return RouteUtils.HaversineMeters(last.Lat, last.Lon, fix.Lat, fix.Lon)
            > _options.OffRouteMinimumDistanceMetersAfterReroute;
    }

    private double DynamicRadius(RouteProgress progress) =>
        progress.UpcomingStep is not null && progress.StepDistanceRemainingMeters <= _options.ManeuverZoneRadiusMeters
            ? _options.OffRouteThresholdRadiusMeters / 2
            : _options.OffRouteThresholdRadiusMeters;

    private static double DistanceFromStep(LocationFix fix, RouteLeg leg, LegStep step)
    {
        if (leg.Shape.Count == 0) return 0;
        int from = Math.Clamp(step.BeginShapeIndex, 0, leg.Shape.Count - 1);
        int to = Math.Clamp(step.EndShapeIndex, from, leg.Shape.Count - 1);
        // The step's own vertices, with one either side when it has only one (an arrival step)
        if (to == from) { from = Math.Max(0, from - 1); to = Math.Min(leg.Shape.Count - 1, to + 1); }
        var shape = new List<(double Lon, double Lat)>(to - from + 1);
        for (int i = from; i <= to; i++) shape.Add(leg.Shape[i]);
        return RouteGeometry.SnapToLine(fix.Lat, fix.Lon, shape, RouteGeometry.Cumulative(shape)).DistanceMeters;
    }

    /// <summary>
    /// Getting further from the next turn: three fixes each further than the last, and further by more than the
    /// wrong-direction distance in all; getting nearer by the right-direction distance starts the count again.
    /// </summary>
    private bool MovingAwayFromManeuver(RouteProgress progress)
    {
        if (progress.UpcomingStep is null) return false;
        double toManeuver = progress.StepDistanceRemainingMeters;
        if (toManeuver <= 0) return false;

        if (_awayFromManeuver.Count == 0)
            _awayFromManeuver.Add(toManeuver);
        else if (toManeuver > _awayFromManeuver[^1])
        {
            // Keep the first, replace the latest
            if (_awayFromManeuver.Count >= WrongWayFixes) _awayFromManeuver.RemoveAt(_awayFromManeuver.Count - 1);
            _awayFromManeuver.Add(toManeuver);
        }
        else if (_awayFromManeuver[^1] - toManeuver > _options.OffRouteMinimumDistanceMetersBeforeRightDirection)
            _awayFromManeuver.Clear();

        return _awayFromManeuver.Count >= WrongWayFixes &&
               _awayFromManeuver[^1] - _awayFromManeuver[0] > _options.OffRouteMinimumDistanceMetersBeforeWrongDirection;
    }
}
