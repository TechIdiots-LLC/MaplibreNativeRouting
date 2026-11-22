using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Navigation;

/// <summary>
/// Decides when to announce the turn ahead — the maneuver that begins the next step, where the current step ends —
/// and what to say. Three announcements a turn, far, medium and near (the near one "imminent"): about 60, 15 and 4
/// seconds ahead at the speed being driven, but never nearer than 1000, 200 and 30 metres. Where a step is already
/// inside a nearer one when it begins, only that one is made.
///
/// Inspired by maplibre-navigation-android VoiceInstructionMilestone / BannerInstructionMilestone.
/// </summary>
public class ManeuverAnnouncerHelper
{
    private static readonly double[] MinimumMeters = [1000, 200, 30];
    private static readonly double[] SecondsAhead = [60, 15, 4];
    private const double FallbackSpeedMps = 13.9;   // 50 km/h, when neither the fix nor the step says
    private const double ShortNameMeters = 100;     // a road name that lasts less than this isn't worth saying

    private readonly DistanceFormatter _distances;
    private int _lastFiredThresholdIndex = -1;
    private int _lastStepIndex = -1;

    public ManeuverAnnouncerHelper(DistanceFormatter? distances = null) => _distances = distances ?? new DistanceFormatter();

    public event EventHandler<ManeuverAnnouncementEventArgs>? AnnouncementNeeded;

    /// <summary>Call on every RouteProgress update. Fires AnnouncementNeeded when the
    /// user crosses a distance threshold for the next maneuver.</summary>
    public void Update(RouteProgress progress)
    {
        // Reset thresholds on step change.
        if (progress.CurrentStepIndex != _lastStepIndex)
        {
            _lastStepIndex = progress.CurrentStepIndex;
            _lastFiredThresholdIndex = -1;
        }

        // The turn ahead is the next step's; the last step has none (arrival is announced on its own)
        if (progress.UpcomingStep is not { } maneuver) return;
        double dist = progress.StepDistanceRemainingMeters;
        double speed = progress.Location.SpeedMps is double v && v > 0 ? v
            : progress.CurrentStep is { Duration: > 0, Distance: > 0 } st ? st.Distance / st.Duration
            : FallbackSpeedMps;

        // The nearest threshold already reached
        int reached = -1;
        for (int i = 0; i < MinimumMeters.Length; i++)
            if (dist <= Math.Max(MinimumMeters[i], speed * SecondsAhead[i])) reached = i;
        if (reached <= _lastFiredThresholdIndex) return;
        _lastFiredThresholdIndex = reached;

        bool imminent = reached == MinimumMeters.Length - 1;
        // At the destination itself, the arrival is announced instead
        if (imminent && maneuver.Type is ManeuverType.Destination or ManeuverType.DestinationLeft or ManeuverType.DestinationRight)
            return;
        // Going straight on into a new name isn't a turn: said once, as it's reached, and not at all for a name
        // that lasts only a few metres
        if (maneuver.Type == ManeuverType.Continue && (!imminent || maneuver.Distance < ShortNameMeters))
            return;
        AnnouncementNeeded?.Invoke(this, new ManeuverAnnouncementEventArgs
        {
            Step = maneuver,
            DistanceMeters = dist,
            Threshold = Math.Max(MinimumMeters[reached], speed * SecondsAhead[reached]),
            IsImminent = imminent,
            Text = SpokenInstructions.Maneuver(maneuver, dist, _distances, imminent),
        });
    }
}

public class ManeuverAnnouncementEventArgs : EventArgs
{
    /// <summary>The maneuver announced: the step that begins at the turn ahead.</summary>
    public required LegStep Step { get; init; }
    /// <summary>To the turn, metres.</summary>
    public double DistanceMeters { get; init; }
    public double Threshold { get; init; }
    /// <summary>The last announcement before the turn: "Turn left onto Main Street".</summary>
    public bool IsImminent { get; init; }
    /// <summary>What to say: "In 400 feet, turn left onto Main Street".</summary>
    public string Text { get; init; } = "";
}
