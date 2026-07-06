using System.Diagnostics;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Navigation;
using MaplibreNative.Routing.Core.Routing;

namespace MaplibreNative.Routing.Wpf;

/// <summary>
/// Ties together route calculation, RouteProgressTracker, ManeuverAnnouncerHelper,
/// and the map overlay for WPF host apps.
///
/// Unlike the MAUI version, GPS is not subscribed to internally — the host app
/// calls UpdateLocation() whenever it receives a new GPS fix. This keeps the
/// session decoupled from any particular GPS provider (VistumblerCS, Windows
/// Location API, NMEA serial, etc.).
///
/// Lifecycle: Create → StartAsync → host calls UpdateLocation() repeatedly → StopAsync → Dispose.
/// </summary>
public class NavigationSession : IAsyncDisposable
{
    private readonly IRouteDataSource _dataSource;
    private readonly RouteOverlay _overlay;

    private RouteProgressTracker? _tracker;
    private ManeuverAnnouncerHelper? _announcer;
    private DirectionsRoute? _activeRoute;

    public event EventHandler<RouteProgress>? ProgressUpdated;
    public event EventHandler<ManeuverAnnouncementEventArgs>? AnnouncementNeeded;

    public DirectionsRoute? ActiveRoute => _activeRoute;
    public bool IsNavigating => _activeRoute is not null;

    public NavigationSession(IRouteDataSource dataSource, RouteOverlay overlay)
    {
        _dataSource = dataSource;
        _overlay = overlay;
    }

    /// <summary>Calculates a route and shows it on the map overlay.</summary>
    public async Task<DirectionsRoute?> StartAsync(RouteOptions options)
    {
        var tracks = await _dataSource.GetRoutableTrackFeaturesAsync();
        var optionsWithTracks = options with { TrackFeatures = tracks };

        IRoutingEngine engine = options.Profile switch
        {
            RouteProfile.TrackOnly => new TrackGraphRouter(),
            RouteProfile.HybridMotorcycle or RouteProfile.HybridBicycle => new HybridRouter(),
            RouteProfile.HybridOfflineMotorcycle or RouteProfile.HybridOfflineBicycle => new HybridRouter(new MvtRouter()),
            RouteProfile.OfflineAuto or RouteProfile.OfflineBicycle or RouteProfile.OfflinePedestrian => new MvtRouter(),
            _ => new ValhallaMtbRouter(),
        };

        DirectionsRoute? route;
        try { route = await engine.RouteAsync(optionsWithTracks); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NavigationSession] Route failed: {ex}");
            return null;
        }

        if (route is null) return null;

        _activeRoute = route;
        _tracker = new RouteProgressTracker(route);
        _announcer = new ManeuverAnnouncerHelper();
        _announcer.AnnouncementNeeded += (s, e) => AnnouncementNeeded?.Invoke(s, e);
        _overlay.ShowRoute(route);
        return route;
    }

    /// <summary>
    /// Call this from your GPS handler (e.g. LocationChanged, NMEA parser) to
    /// advance the route progress tracker and fire ProgressUpdated.
    /// </summary>
    public void UpdateLocation(double latitude, double longitude)
    {
        if (_tracker is null || _activeRoute is null) return;
        var progress = _tracker.Update(latitude, longitude);
        _announcer?.Update(progress);
        ProgressUpdated?.Invoke(this, progress);
    }

    /// <summary>Stops navigation and removes the route overlay.</summary>
    public Task StopAsync()
    {
        _activeRoute = null;
        _tracker = null;
        _announcer = null;
        _overlay.ClearRoute();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
