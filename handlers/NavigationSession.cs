using System.Diagnostics;
using MapLibreNative.Maui.Handlers;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Navigation;
using MaplibreNative.Routing.Core.Routing;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing;

/// <summary>
/// Ties together: route calculation, live GPS tracking, RouteProgressTracker,
/// ManeuverAnnouncerHelper, and the map overlay. One session per active navigation.
/// Lifecycle: Create → StartAsync → (GPS updates) → StopAsync → Dispose.
/// </summary>
public class NavigationSession : IAsyncDisposable
{
    private readonly IRouteDataSource _dataSource;
    private readonly RouteOverlay _overlay;

    private RouteProgressTracker? _tracker;
    private ManeuverAnnouncerHelper? _announcer;
    private DirectionsRoute? _activeRoute;
    private bool _listening;

    // Fires on the main thread whenever progress updates.
    public event EventHandler<RouteProgress>? ProgressUpdated;

    // Fires when a maneuver announcement is needed.
    public event EventHandler<ManeuverAnnouncementEventArgs>? AnnouncementNeeded;

    public DirectionsRoute? ActiveRoute => _activeRoute;
    public bool IsNavigating => _activeRoute is not null && _listening;

    public NavigationSession(IRouteDataSource dataSource, RouteOverlay overlay)
    {
        _dataSource = dataSource;
        _overlay = overlay;
    }

    /// <summary>Calculates a route and starts listening for GPS updates.</summary>
    public async Task<DirectionsRoute?> StartAsync(RouteOptions options)
    {
        // Only fetch track features from the data source if the caller hasn't already
        // provided them (e.g. MapViewModel pre-populates them to avoid a double fetch).
        var tracks = options.TrackFeatures.Count > 0
            ? options.TrackFeatures
            : await _dataSource.GetRoutableTrackFeaturesAsync();
        var optionsWithTracks = options with { TrackFeatures = tracks };

        IRoutingEngine engine = options.Profile switch
        {
            RouteProfile.TrackOnly => new TrackGraphRouter(),
            RouteProfile.HybridMotorcycle or RouteProfile.HybridBicycle => new HybridRouter(),
            RouteProfile.HybridOfflineMotorcycle or RouteProfile.HybridOfflineBicycle => new HybridRouter(new MvtRouter()),
            RouteProfile.OfflineAuto or RouteProfile.OfflineBicycle or RouteProfile.OfflinePedestrian => new MvtRouter(),
            _ => new ValhallaMtbRouter(),
        };

        // Run the engine on a thread-pool thread so the MAUI main thread stays free
        // to process IProgress<string> callbacks while routing is in progress.
        DirectionsRoute? route;
        try { route = await Task.Run(() => engine.RouteAsync(optionsWithTracks)); }
        catch (OperationCanceledException) { throw; }  // let caller show timeout message
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

        Geolocation.LocationChanged += OnLocationChanged;
        if (!Geolocation.IsListeningForeground)
        {
            var req = new GeolocationListeningRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(1));
            await Geolocation.StartListeningForegroundAsync(req);
        }
        _listening = true;

        return route;
    }

    /// <summary>Stops navigation and removes the route overlay.</summary>
    public async Task StopAsync()
    {
        Geolocation.LocationChanged -= OnLocationChanged;
        if (_listening && Geolocation.IsListeningForeground)
            Geolocation.StopListeningForeground();
        _listening = false;
        _activeRoute = null;
        _tracker = null;
        _announcer = null;
        _overlay.ClearRoute();
        await Task.CompletedTask;
    }

    private void OnLocationChanged(object? sender, GeolocationLocationChangedEventArgs e)
    {
        if (_tracker is null || _activeRoute is null) return;
        var loc = e.Location;

        var progress = _tracker.Update(loc.Latitude, loc.Longitude);
        _announcer?.Update(progress);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            ProgressUpdated?.Invoke(this, progress);
        });
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
