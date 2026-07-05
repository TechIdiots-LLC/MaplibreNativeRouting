using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Navigation;

/// <summary>
/// Tracks the user's position along an active route. On each location fix, it:
///   1. Snaps the position to the nearest route shape coordinate.
///   2. Determines the current leg and step.
///   3. Computes remaining distance/duration and fraction traveled.
///   4. Advances the step index when the user passes the step's end threshold.
///
/// Ported from maplibre-navigation-android's NavigationRouteProcessor / RouteProgress logic.
/// </summary>
public class RouteProgressTracker
{
    private const double StepAdvanceThresholdM = 30; // snap within this to advance to next step

    private DirectionsRoute _route;
    private int _legIndex;
    private int _stepIndex;

    public RouteProgressTracker(DirectionsRoute route)
    {
        _route = route;
        _legIndex = 0;
        _stepIndex = 0;
    }

    /// <summary>Updates with a new GPS fix and returns the current RouteProgress.</summary>
    public RouteProgress Update(double lat, double lon)
    {
        var leg = _route.Legs[_legIndex];

        // Snap to the full leg shape.
        int snappedIdx = RouteUtils.SnapToShape(lat, lon, leg.Shape,
            out double snappedLon, out double snappedLat);

        // Advance step if we're past the current step's end.
        TryAdvanceStep(leg, snappedIdx);

        // Recompute current step after potential advance.
        var step = leg.Steps[_stepIndex];

        double stepDistRemaining = DistanceFromIndexToEnd(leg.Shape, snappedIdx, step.EndShapeIndex);
        double totalDistRemaining = ComputeRemainingDistance(snappedIdx);
        double totalDist = _route.Distance > 0 ? _route.Distance : 1;
        double distTraveled = totalDist - totalDistRemaining;

        return new RouteProgress
        {
            Route = _route,
            CurrentLegIndex = _legIndex,
            CurrentStepIndex = _stepIndex,
            DistanceRemainingMeters = totalDistRemaining,
            DurationRemainingSeconds = EstimateDuration(totalDistRemaining),
            StepDistanceRemainingMeters = stepDistRemaining,
            FractionTraveled = Math.Clamp(distTraveled / totalDist, 0, 1),
            SnappedLocation = (snappedLon, snappedLat),
        };
    }

    public void SetRoute(DirectionsRoute newRoute)
    {
        _route = newRoute;
        _legIndex = 0;
        _stepIndex = 0;
    }

    private void TryAdvanceStep(RouteLeg leg, int snappedIdx)
    {
        if (_stepIndex >= leg.Steps.Count - 1) return;
        var step = leg.Steps[_stepIndex];
        double distToEnd = DistanceFromIndexToEnd(leg.Shape, snappedIdx, step.EndShapeIndex);
        if (distToEnd <= StepAdvanceThresholdM)
            _stepIndex++;
    }

    private double DistanceFromIndexToEnd(
        IReadOnlyList<(double Lon, double Lat)> shape,
        int fromIndex, int toIndex)
    {
        double dist = 0;
        int end = Math.Min(toIndex, shape.Count - 1);
        for (int i = fromIndex; i < end; i++)
            dist += RouteUtils.HaversineMeters(shape[i].Lat, shape[i].Lon,
                                               shape[i + 1].Lat, shape[i + 1].Lon);
        return dist;
    }

    private double ComputeRemainingDistance(int snappedIdxInCurrentLeg)
    {
        double dist = 0;
        var legs = _route.Legs;

        // Remaining in current leg from snapped index.
        var curLeg = legs[_legIndex];
        dist += DistanceFromIndexToEnd(curLeg.Shape, snappedIdxInCurrentLeg, curLeg.Shape.Count - 1);

        // Future legs.
        for (int li = _legIndex + 1; li < legs.Count; li++)
            dist += legs[li].Distance;

        return dist;
    }

    private double EstimateDuration(double remainingMeters)
    {
        // Rough estimate: use the route's average speed.
        if (_route.Distance <= 0 || _route.Duration <= 0) return 0;
        double metersPerSecond = _route.Distance / _route.Duration;
        return remainingMeters / metersPerSecond;
    }
}
