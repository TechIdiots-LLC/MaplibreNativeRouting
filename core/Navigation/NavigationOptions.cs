namespace MaplibreNative.Routing.Core.Navigation;

/// <summary>
/// How a <see cref="NavigationEngine"/> decides the user has left the route, and what it does then. The defaults are
/// maplibre-navigation-android's (MapLibreNavigationOptions, NOTICE.md).
/// </summary>
public sealed record NavigationOptions
{
    /// <summary>Further than this from the current step's line is off the route (half of it near a turn).</summary>
    public double OffRouteThresholdRadiusMeters { get; init; } = 50;

    /// <summary>After a reroute, or at the start, the user must move this far before being off the route again.</summary>
    public double OffRouteMinimumDistanceMetersAfterReroute { get; init; } = 50;

    /// <summary>Getting this much further from the next turn, over three fixes, is going the wrong way.</summary>
    public double OffRouteMinimumDistanceMetersBeforeWrongDirection { get; init; } = 50;

    /// <summary>Getting this much nearer the next turn undoes the wrong-way count.</summary>
    public double OffRouteMinimumDistanceMetersBeforeRightDirection { get; init; } = 20;

    /// <summary>Within this of the next turn's line, a user off the current step has only turned a little early.</summary>
    public double ManeuverZoneRadiusMeters { get; init; } = 40;

    /// <summary>The fix's accuracy times this widens the off-route radius: a 60 m accuracy allows 60 m.</summary>
    public double DeadReckoningTimeInterval { get; init; } = 1;

    /// <summary>Plan a new route from where the user is when they leave this one.</summary>
    public bool AutoReroute { get; init; } = true;

    /// <summary>The least time between two reroutes (maplibre-navigation-android's SECONDS_BEFORE_REROUTE).</summary>
    public TimeSpan MinimumTimeBetweenReroutes { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>How long a reroute may take before it's given up.</summary>
    public TimeSpan RerouteTimeout { get; init; } = TimeSpan.FromSeconds(60);
}
