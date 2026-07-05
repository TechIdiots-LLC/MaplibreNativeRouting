namespace MaplibreNative.Routing.Core.Models;

/// <summary>Snapshot of where the user is along an active route. Updated on each
/// location fix by RouteProgressTracker. Inspired by maplibre-navigation-android's
/// RouteProgress model.</summary>
public class RouteProgress
{
    public required DirectionsRoute Route { get; init; }

    /// <summary>Index of the current leg (0-based).</summary>
    public int CurrentLegIndex { get; init; }

    /// <summary>Index of the current step within the current leg.</summary>
    public int CurrentStepIndex { get; init; }

    /// <summary>Distance remaining to the end of the route, meters.</summary>
    public double DistanceRemainingMeters { get; init; }

    /// <summary>Estimated time remaining to the end of the route, seconds.</summary>
    public double DurationRemainingSeconds { get; init; }

    /// <summary>Distance remaining to the end of the current step, meters.</summary>
    public double StepDistanceRemainingMeters { get; init; }

    /// <summary>0.0–1.0 fraction of total route distance traveled.</summary>
    public double FractionTraveled { get; init; }

    /// <summary>Snapped position on the route geometry.</summary>
    public (double Lon, double Lat) SnappedLocation { get; init; }

    public RouteLeg CurrentLeg => Route.Legs[CurrentLegIndex];
    public LegStep CurrentStep => CurrentLeg.Steps[CurrentStepIndex];
    public LegStep? UpcomingStep
    {
        get
        {
            var nextIdx = CurrentStepIndex + 1;
            return nextIdx < CurrentLeg.Steps.Count ? CurrentLeg.Steps[nextIdx] : null;
        }
    }
}
