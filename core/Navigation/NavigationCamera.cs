using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Navigation;

/// <summary>How the camera follows navigation.</summary>
public enum NavigationCameraMode
{
    /// <summary>Behind the user, turned to the road ahead, tilted, zoomed to show the next turn.</summary>
    Following,
    /// <summary>Over the user, north up, flat.</summary>
    NorthUp,
    /// <summary>The rest of the route, from the user to the destination, north up.</summary>
    Overview,
}

/// <summary>
/// Where the camera should be. The padding (screen pixels) places the target in the view: with a top padding, it
/// sits in the middle of what's below it, lower on the screen.
/// </summary>
public readonly record struct NavigationCameraPosition(
    double Lat, double Lon, double Zoom, double Bearing, double Pitch,
    double PaddingTop = 0, double PaddingBottom = 0);

/// <summary>
/// Works out where the camera should be as navigation goes, the same on every platform; a platform's controller moves
/// its map there. Following, the user sits low in the view (<see cref="FollowAnchor"/>) so more of the road ahead
/// shows, the view turns with the road a little ahead of them (steadier than the GPS's course), tilts more the
/// further the next turn is, and zooms to fit the user and the next turn, changing zoom only at a new step and as
/// the turn nears, not on every fix.
///
/// The tilt and zoom are ported from maplibre-navigation-android's DynamicCamera, with its limits (zoom 12 to 16,
/// tilt 45° to 60°) and alert times (125, 70 and 15 seconds before the turn) (NOTICE.md).
/// </summary>
public sealed class NavigationCamera
{
    public const double DefaultZoom = 15, MinZoom = 12, MaxZoom = 16;
    public const double MinTilt = 45, MaxTilt = 60;
    private static readonly double[] AlertSeconds = [125, 70, 15];
    private const double LookAheadMeters = 40;   // the road's direction is taken this far ahead
    private const double OnRouteMeters = 30;     // nearer than this, the camera follows the route, not the raw fix
    private const double WorldTileSize = 512;    // MapLibre's world is 512 × 2^zoom pixels across

    private DirectionsRoute? _route;
    private int _legIndex = -1;
    private double[]? _cumulative;
    private int _stepIndex = -1;
    private int _alertsPassed;
    private double _zoom = DefaultZoom;
    private double _bearing;

    public NavigationCameraMode Mode { get; set; } = NavigationCameraMode.Following;

    /// <summary>The map's size, device-independent pixels.</summary>
    public double ViewportWidth { get; set; } = 400;
    public double ViewportHeight { get; set; } = 700;

    /// <summary>Screen covered by the app's own panels: above (an instruction banner), and below (a sheet).</summary>
    public double TopInset { get; set; }
    public double BottomInset { get; set; }

    /// <summary>Following, where the user sits from the top of the view: 0.5 the middle, 0.7 lower down.</summary>
    public double FollowAnchor { get; set; } = 0.7;

    /// <summary>The camera for this point of the drive.</summary>
    public NavigationCameraPosition For(RouteProgress progress)
    {
        if (Mode == NavigationCameraMode.Overview) return Overview(progress);
        Track(progress);

        bool onRoute = progress.DistanceFromRouteMeters <= OnRouteMeters;
        var (lon, lat) = onRoute ? progress.SnappedLocation : (progress.Location.Lon, progress.Location.Lat);
        double bearing = onRoute ? RoadBearing(progress) : progress.Location.Bearing ?? _bearing;
        _bearing = bearing;

        if (Mode == NavigationCameraMode.NorthUp)
            return new NavigationCameraPosition(lat, lon, _zoom, 0, 0, TopInset, BottomInset);

        // The user sits FollowAnchor down the part of the view left between the panels
        double free = Math.Max(1, ViewportHeight - TopInset - BottomInset);
        double padTop = TopInset + Math.Max(0, free * (2 * FollowAnchor - 1));
        return new NavigationCameraPosition(lat, lon, _zoom, bearing,
            Math.Clamp(Math.Round(progress.StepDistanceRemainingMeters / 5), MinTilt, MaxTilt), padTop, BottomInset);
    }

    /// <summary>The rest of the route, from the user to the destination, fitted to the view.</summary>
    public NavigationCameraPosition Overview(RouteProgress progress)
    {
        var points = new List<(double Lon, double Lat)> { (progress.Location.Lon, progress.Location.Lat) };
        var leg = progress.CurrentLeg;
        var cum = Cumulative(progress);
        for (int i = 0; i < leg.Shape.Count; i++)
            if (cum.Length == leg.Shape.Count && cum[i] >= progress.LegDistanceTraveledMeters) points.Add(leg.Shape[i]);
        for (int li = progress.CurrentLegIndex + 1; li < progress.Route.Legs.Count; li++) points.AddRange(progress.Route.Legs[li].Shape);
        var (lat, lon, zoom) = Fit(points, 40, 20);
        return new NavigationCameraPosition(lat, lon, Math.Min(zoom, MaxZoom), 0, 0, TopInset, BottomInset);
    }

    /// <summary>A new step, or one of its alert times passed: the zoom is fitted again to the user and the turn.</summary>
    private void Track(RouteProgress progress)
    {
        bool fit = false;
        if (progress.CurrentStepIndex != _stepIndex || !ReferenceEquals(progress.Route, _route) || progress.CurrentLegIndex != _legIndex)
        {
            _stepIndex = progress.CurrentStepIndex;
            _alertsPassed = 0;
            fit = true;
        }
        var step = progress.CurrentStep;
        double speed = step is { Duration: > 0, Distance: > 0 } ? step.Distance / step.Duration : 13.9;
        double secondsLeft = progress.StepDistanceRemainingMeters / speed;
        while (_alertsPassed < AlertSeconds.Length && secondsLeft < AlertSeconds[_alertsPassed])
        {
            if (step.Duration > AlertSeconds[_alertsPassed]) fit = true;
            _alertsPassed++;
        }
        if (!fit) return;

        var leg = progress.CurrentLeg;
        if (progress.UpcomingStep is null || leg.Shape.Count == 0) return;
        var turn = leg.Shape[Math.Clamp(step.EndShapeIndex, 0, leg.Shape.Count - 1)];
        var (_, _, zoom) = Fit([(progress.Location.Lon, progress.Location.Lat), turn], 0, 0);
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
    }

    /// <summary>The direction of the road from the user to a little ahead of them.</summary>
    private double RoadBearing(RouteProgress progress)
    {
        var leg = progress.CurrentLeg;
        var cum = Cumulative(progress);
        if (leg.Shape.Count < 2 || cum.Length != leg.Shape.Count) return progress.Location.Bearing ?? _bearing;
        double along = progress.LegDistanceTraveledMeters;
        var here = RouteGeometry.PointAlong(leg.Shape, cum, along);
        var ahead = RouteGeometry.PointAlong(leg.Shape, cum, Math.Min(cum[^1], along + LookAheadMeters));
        if (RouteUtils.HaversineMeters(here.Lat, here.Lon, ahead.Lat, ahead.Lon) < 1) return here.Bearing;
        return RouteUtils.InitialBearing(here.Lat, here.Lon, ahead.Lat, ahead.Lon);
    }

    private double[] Cumulative(RouteProgress progress)
    {
        if (!ReferenceEquals(progress.Route, _route) || progress.CurrentLegIndex != _legIndex || _cumulative is null)
        {
            _route = progress.Route;
            _legIndex = progress.CurrentLegIndex;
            _cumulative = RouteGeometry.Cumulative(progress.CurrentLeg.Shape);
        }
        return _cumulative;
    }

    /// <summary>The centre and zoom that fit some points in the view, less a margin (pixels) and the panels.</summary>
    private (double Lat, double Lon, double Zoom) Fit(IReadOnlyList<(double Lon, double Lat)> points, double margin, double minSpanPx)
    {
        double MercY(double lat)
        {
            double s = Math.Sin(Math.Clamp(lat, -85.05, 85.05) * Math.PI / 180);
            return 0.5 - Math.Log((1 + s) / (1 - s)) / (4 * Math.PI);
        }
        double minX = points.Min(p => (p.Lon + 180) / 360), maxX = points.Max(p => (p.Lon + 180) / 360);
        double minY = points.Min(p => MercY(p.Lat)), maxY = points.Max(p => MercY(p.Lat));
        double width = Math.Max(1, ViewportWidth - 2 * margin);
        double height = Math.Max(1, ViewportHeight - TopInset - BottomInset - 2 * margin);
        double spanX = Math.Max(maxX - minX, minSpanPx / (WorldTileSize * Math.Pow(2, MaxZoom)));
        double spanY = Math.Max(maxY - minY, minSpanPx / (WorldTileSize * Math.Pow(2, MaxZoom)));
        double zoom = Math.Log2(Math.Min(width / (spanX * WorldTileSize), height / (spanY * WorldTileSize)));
        double cy = (minY + maxY) / 2;
        double lat = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * cy))) * 180 / Math.PI;
        return (lat, (minX + maxX) / 2 * 360 - 180, double.IsFinite(zoom) ? zoom : MaxZoom);
    }
}
