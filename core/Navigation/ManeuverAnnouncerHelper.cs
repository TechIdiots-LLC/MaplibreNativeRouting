using MaplibreNative.Routing.Core.Models;

namespace MaplibreNative.Routing.Core.Navigation;

/// <summary>
/// Determines when to trigger voice or banner instructions based on distance to the
/// next maneuver. Fires callbacks at three distance thresholds:
///   - Far (e.g. 1000 m / 0.5 mi) — "In 1 kilometer, turn right"
///   - Medium (e.g. 200 m / 660 ft)  — "In 200 meters, turn right"
///   - Near (e.g. 30 m)              — "Turn right now"
///
/// Inspired by maplibre-navigation-android VoiceInstructionMilestone / BannerInstructionMilestone.
/// </summary>
public class ManeuverAnnouncerHelper
{
    private static readonly double[] Thresholds = [1000, 200, 30];

    private int _lastFiredThresholdIndex = -1;
    private int _lastStepIndex = -1;

    public event EventHandler<ManeuverAnnouncementEventArgs>? AnnouncementNeeded;

    /// <summary>Call on every RouteProgress update. Fires AnnouncementNeeded when the
    /// user crosses a distance threshold for the current maneuver.</summary>
    public void Update(RouteProgress progress)
    {
        var step = progress.CurrentStep;
        var dist = progress.StepDistanceRemainingMeters;

        // Reset thresholds on step change.
        if (progress.CurrentStepIndex != _lastStepIndex)
        {
            _lastStepIndex = progress.CurrentStepIndex;
            _lastFiredThresholdIndex = -1;
        }

        for (int i = 0; i < Thresholds.Length; i++)
        {
            if (i <= _lastFiredThresholdIndex) continue;
            if (dist <= Thresholds[i])
            {
                _lastFiredThresholdIndex = i;
                AnnouncementNeeded?.Invoke(this, new ManeuverAnnouncementEventArgs
                {
                    Step = step,
                    DistanceMeters = dist,
                    Threshold = Thresholds[i],
                    IsImminent = i == Thresholds.Length - 1,
                });
                break; // fire at most one threshold per update
            }
        }
    }
}

public class ManeuverAnnouncementEventArgs : EventArgs
{
    public required LegStep Step { get; init; }
    public double DistanceMeters { get; init; }
    public double Threshold { get; init; }
    public bool IsImminent { get; init; }
}
