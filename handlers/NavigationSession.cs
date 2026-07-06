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
    private List<DirectionsRoute> _routeAlternatives = [];
    private int _selectedRouteIndex;

    // Fires on the main thread whenever progress updates.
    public event EventHandler<RouteProgress>? ProgressUpdated;

    // Fires when a maneuver announcement is needed.
    public event EventHandler<ManeuverAnnouncementEventArgs>? AnnouncementNeeded;

    public DirectionsRoute? ActiveRoute => _activeRoute;
    public IReadOnlyList<DirectionsRoute> RouteAlternatives => _routeAlternatives;
    public int SelectedRouteIndex => _selectedRouteIndex;
    public bool IsNavigating => _activeRoute is not null && _listening;

    public NavigationSession(IRouteDataSource dataSource, RouteOverlay overlay)
    {
        _dataSource = dataSource;
        _overlay = overlay;
    }

    /// <summary>Calculates up to <paramref name="maxRoutes"/> alternative routes (sorted shortest
    /// first) and starts listening for GPS updates on the shortest one.</summary>
    public async Task<DirectionsRoute?> StartAsync(RouteOptions options, int maxRoutes = 1)
    {
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

        // Run on a thread-pool thread so the MAUI main thread stays free to handle IProgress callbacks.
        List<DirectionsRoute> alternatives;
        try { alternatives = await Task.Run(() => engine.FindAlternativesAsync(optionsWithTracks, maxRoutes)); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NavigationSession] Route failed: {ex}");
            return null;
        }

        if (alternatives.Count == 0) return null;

        _routeAlternatives = alternatives;
        _selectedRouteIndex = 0;
        var route = alternatives[0];

        _activeRoute = route;
        _tracker = new RouteProgressTracker(route);
        _announcer = new ManeuverAnnouncerHelper();
        _announcer.AnnouncementNeeded += (s, e) => AnnouncementNeeded?.Invoke(s, e);

        _overlay.ShowRoutes(_routeAlternatives, 0);

        Geolocation.LocationChanged += OnLocationChanged;
        if (!Geolocation.IsListeningForeground)
        {
            var req = new GeolocationListeningRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(1));
            await Geolocation.StartListeningForegroundAsync(req);
        }
        _listening = true;

        return route;
    }

    /// <summary>Switch to a different computed alternative without re-routing.</summary>
    public void SelectAlternative(int index)
    {
        if (index < 0 || index >= _routeAlternatives.Count) return;
        _selectedRouteIndex = index;
        _activeRoute = _routeAlternatives[index];
        _tracker = new RouteProgressTracker(_activeRoute);
        _overlay.ShowRoutes(_routeAlternatives, index);
    }

    /// <summary>Stops navigation and removes the route overlay.</summary>
    public async Task StopAsync()
    {
        Geolocation.LocationChanged -= OnLocationChanged;
        if (_listening && Geolocation.IsListeningForeground)
            Geolocation.StopListeningForeground();
        _listening = false;
        _activeRoute = null;
        _routeAlternatives = [];
        _selectedRouteIndex = 0;
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
