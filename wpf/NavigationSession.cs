using System.Diagnostics;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Navigation;

namespace MaplibreNative.Routing.Wpf;

/// <summary>
/// Navigation in a WPF app: a <see cref="NavigationEngine"/>, with the route drawn by the <see cref="RouteOverlay"/>.
///
/// Unlike the MAUI version, GPS is not subscribed to internally — the host app
/// calls UpdateLocation() whenever it receives a new GPS fix. This keeps the
/// session decoupled from any particular GPS provider (VistumblerCS, Windows
/// Location API, NMEA serial, etc.). Or <see cref="StartSimulation"/> drives the route as simulated GPS.
///
/// Events are raised on the thread that called UpdateLocation; a reroute's, and a simulated drive's, on the
/// synchronization context the session was created on (the UI thread, in a WPF app).
///
/// Lifecycle: Create → StartAsync → host calls UpdateLocation() repeatedly → StopAsync → Dispose.
/// </summary>
public class NavigationSession : IAsyncDisposable
{
    private readonly IRouteDataSource _dataSource;
    private readonly RouteOverlay _overlay;
    private readonly NavigationEngine _engine;
    private readonly SynchronizationContext? _ui;
    private RouteSimulator? _simulator;

    public event EventHandler<RouteProgress>? ProgressUpdated;
    public event EventHandler<ManeuverAnnouncementEventArgs>? AnnouncementNeeded;
    /// <summary>The user has left the route; with <see cref="NavigationOptions.AutoReroute"/>, a new one is being planned.</summary>
    public event EventHandler<OffRouteEventArgs>? OffRoute;
    /// <summary>A new route from where the user left the last one; already drawn and being followed.</summary>
    public event EventHandler<DirectionsRoute>? Rerouted;
    public event EventHandler<RerouteFailedEventArgs>? RerouteFailed;
    public event EventHandler<RouteProgress>? Arrived;
    /// <summary>Each instruction as it's due, for showing (Voice speaks them).</summary>
    public event EventHandler<string>? VoiceInstruction;
    /// <summary>Each position of a simulated drive, for the app to draw.</summary>
    public event EventHandler<LocationFix>? SimulatedLocationChanged;
    public event EventHandler? SimulationFinished;

    public NavigationEngine Engine => _engine;
    /// <summary>Speaks the instructions once its IsEnabled is set.</summary>
    public VoiceGuidance Voice { get; }
    public DirectionsRoute? ActiveRoute => _engine.Route;
    public bool IsNavigating => _engine.Route is not null;
    public bool IsSimulating => _simulator is not null;

    public NavigationSession(IRouteDataSource dataSource, RouteOverlay overlay, NavigationOptions? options = null)
    {
        _dataSource = dataSource;
        _overlay = overlay;
        _ui = SynchronizationContext.Current;
        _engine = new NavigationEngine(options);
        Voice = new VoiceGuidance(_engine);
        _engine.ProgressUpdated += (_, p) => ProgressUpdated?.Invoke(this, p);
        _engine.AnnouncementNeeded += (_, e) => AnnouncementNeeded?.Invoke(this, e);
        _engine.OffRoute += (_, e) => OffRoute?.Invoke(this, e);
        _engine.Arrived += (_, p) => Arrived?.Invoke(this, p);
        _engine.VoiceInstruction += (_, text) => OnUi(() => VoiceInstruction?.Invoke(this, text));
        _engine.RerouteFailed += (_, e) => OnUi(() => RerouteFailed?.Invoke(this, e));
        _engine.Rerouted += (_, route) => OnUi(() =>
        {
            _overlay.ShowRoute(route);
            Rerouted?.Invoke(this, route);
        });
    }

    /// <summary>Calculates a route and shows it on the map overlay.</summary>
    public async Task<DirectionsRoute?> StartAsync(RouteOptions options)
    {
        var tracks = await _dataSource.GetRoutableTrackFeaturesAsync();
        var optionsWithTracks = options with { TrackFeatures = tracks };

        IReadOnlyList<DirectionsRoute> routes;
        try { routes = await _engine.PlanAsync(optionsWithTracks); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NavigationSession] Route failed: {ex}");
            return null;
        }
        if (routes.Count == 0) return null;

        _overlay.ShowRoute(routes[0]);
        return routes[0];
    }

    /// <summary>
    /// Call this from your GPS handler (e.g. LocationChanged, NMEA parser) to
    /// advance the route progress tracker and fire ProgressUpdated.
    /// </summary>
    public void UpdateLocation(double latitude, double longitude) => UpdateLocation(new LocationFix(latitude, longitude));

    /// <summary>A fix with its accuracy, bearing and speed, when the GPS gives them: accuracy widens the off-route radius.</summary>
    public void UpdateLocation(LocationFix fix)
    {
        if (!IsSimulating) _engine.UpdateLocation(fix);
    }

    /// <summary>
    /// Drives the route (or <paramref name="path"/>: another route, to try leaving this one) as simulated GPS, in
    /// place of UpdateLocation, <paramref name="speedFactor"/> times faster than real time.
    /// </summary>
    public void StartSimulation(double speedFactor = 1, DirectionsRoute? path = null)
    {
        var route = path ?? _engine.Route;
        if (route is null) return;
        StopSimulation();
        var sim = RouteSimulator.ForRoute(route);
        sim.SpeedFactor = speedFactor;
        sim.LocationChanged += (_, fix) => OnUi(() =>
        {
            if (!ReferenceEquals(_simulator, sim)) return;
            _engine.UpdateLocation(fix);
            SimulatedLocationChanged?.Invoke(this, fix);
        });
        sim.Finished += (_, _) => OnUi(() =>
        {
            if (ReferenceEquals(_simulator, sim)) SimulationFinished?.Invoke(this, EventArgs.Empty);
        });
        _simulator = sim;
        sim.Start();
    }

    public void StopSimulation()
    {
        var sim = _simulator;
        _simulator = null;
        sim?.Dispose();
    }

    /// <summary>Stops navigation and removes the route overlay.</summary>
    public Task StopAsync()
    {
        StopSimulation();
        Voice.Stop();
        _engine.Stop();
        _overlay.ClearRoute();
        return Task.CompletedTask;
    }

    private void OnUi(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui) action();
        else _ui.Post(_ => action(), null);
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
