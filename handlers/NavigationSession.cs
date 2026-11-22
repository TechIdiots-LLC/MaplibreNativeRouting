using System.Diagnostics;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Navigation;

namespace MaplibreNative.Routing;

/// <summary>
/// Navigation in a MAUI app: a <see cref="NavigationEngine"/> fed by the device's GPS, or by a simulated drive, with
/// the route drawn by the <see cref="RouteOverlay"/>. Every event is raised on the main thread.
/// Lifecycle: Create → StartAsync → (GPS updates, or StartSimulation) → StopAsync → Dispose.
/// </summary>
public class NavigationSession : IAsyncDisposable
{
    private readonly IRouteDataSource _dataSource;
    private readonly RouteOverlay _overlay;
    private readonly NavigationEngine _engine;
    private RouteSimulator? _simulator;
    private bool _listening;

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
    /// <summary>Each position of a simulated drive (the map's own location puck doesn't show them).</summary>
    public event EventHandler<LocationFix>? SimulatedLocationChanged;
    /// <summary>A simulated drive reached its end.</summary>
    public event EventHandler? SimulationFinished;

    public NavigationEngine Engine => _engine;
    /// <summary>Speaks the instructions once its IsEnabled is set.</summary>
    public VoiceGuidance Voice { get; }
    /// <summary>Moves the map with navigation, once given the map's controller (Camera.SetController).</summary>
    public NavigationCameraController Camera { get; }
    public DirectionsRoute? ActiveRoute => _engine.Route;
    public IReadOnlyList<DirectionsRoute> RouteAlternatives => _engine.Alternatives;
    public int SelectedRouteIndex => _engine.SelectedIndex;
    public bool IsNavigating => _engine.Route is not null && (_listening || IsSimulating);
    public bool IsSimulating => _simulator is not null;

    public NavigationSession(IRouteDataSource dataSource, RouteOverlay overlay, NavigationOptions? options = null)
    {
        _dataSource = dataSource;
        _overlay = overlay;
        _engine = new NavigationEngine(options);
        Voice = new VoiceGuidance(_engine);
        Camera = new NavigationCameraController(this);
        _engine.ProgressUpdated += (_, p) => OnMain(() => ProgressUpdated?.Invoke(this, p));
        _engine.AnnouncementNeeded += (_, e) => OnMain(() => AnnouncementNeeded?.Invoke(this, e));
        _engine.OffRoute += (_, e) => OnMain(() => OffRoute?.Invoke(this, e));
        _engine.Arrived += (_, p) => OnMain(() => Arrived?.Invoke(this, p));
        _engine.VoiceInstruction += (_, text) => OnMain(() => VoiceInstruction?.Invoke(this, text));
        _engine.RerouteFailed += (_, e) => OnMain(() => RerouteFailed?.Invoke(this, e));
        _engine.Rerouted += (_, route) => OnMain(() =>
        {
            _overlay.ShowRoutes([route], 0);
            Rerouted?.Invoke(this, route);
        });
    }

    /// <summary>Calculates up to <paramref name="maxRoutes"/> alternative routes (sorted shortest
    /// first) and starts listening for GPS updates on the shortest one.</summary>
    public async Task<DirectionsRoute?> StartAsync(RouteOptions options, int maxRoutes = 1)
    {
        var tracks = options.TrackFeatures.Count > 0
            ? options.TrackFeatures
            : await _dataSource.GetRoutableTrackFeaturesAsync();
        var optionsWithTracks = options with { TrackFeatures = tracks };

        // Run on a thread-pool thread so the MAUI main thread stays free to handle IProgress callbacks.
        IReadOnlyList<DirectionsRoute> alternatives;
        try { alternatives = await Task.Run(() => _engine.PlanAsync(optionsWithTracks, maxRoutes)); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NavigationSession] Route failed: {ex}");
            return null;
        }
        if (alternatives.Count == 0) return null;

        _overlay.ShowRoutes(alternatives, 0);
        await StartListeningAsync();
        return alternatives[0];
    }

    /// <summary>Switch to a different computed alternative without re-routing.</summary>
    public void SelectAlternative(int index)
    {
        if (index < 0 || index >= _engine.Alternatives.Count) return;
        _engine.SelectAlternative(index);
        _overlay.ShowRoutes(_engine.Alternatives, index);
    }

    /// <summary>
    /// Drives the route (or <paramref name="path"/>: another route, to try leaving this one) as simulated GPS, in
    /// place of the device's, <paramref name="speedFactor"/> times faster than real time. The position is drawn on
    /// the map.
    /// </summary>
    public void StartSimulation(double speedFactor = 1, DirectionsRoute? path = null)
    {
        var route = path ?? _engine.Route;
        if (route is null) return;
        StopSimulation();
        StopListening();
        var sim = RouteSimulator.ForRoute(route);
        sim.SpeedFactor = speedFactor;
        sim.LocationChanged += (_, fix) =>
        {
            _engine.UpdateLocation(fix);
            OnMain(() =>
            {
                if (!ReferenceEquals(_simulator, sim)) return;
                _overlay.ShowPosition(fix);
                SimulatedLocationChanged?.Invoke(this, fix);
            });
        };
        sim.Finished += (_, _) => OnMain(() =>
        {
            if (ReferenceEquals(_simulator, sim)) SimulationFinished?.Invoke(this, EventArgs.Empty);
        });
        _simulator = sim;
        sim.Start();
    }

    /// <summary>Ends a simulated drive; the device's GPS is followed again while navigating.</summary>
    public void StopSimulation()
    {
        var sim = _simulator;
        if (sim is null) return;
        _simulator = null;
        sim.Dispose();
        _overlay.ShowPosition(null);
        if (_engine.Route is not null) _ = StartListeningAsync();
    }

    /// <summary>The simulated drive's speed, times real time.</summary>
    public double SimulationSpeedFactor
    {
        get => _simulator?.SpeedFactor ?? 1;
        set { if (_simulator is not null) _simulator.SpeedFactor = value; }
    }

    /// <summary>Stops navigation and removes the route overlay.</summary>
    public async Task StopAsync()
    {
        var sim = _simulator;
        _simulator = null;
        sim?.Dispose();
        StopListening();
        Voice.Stop();
        _engine.Stop();
        _overlay.ClearRoute();
        await Task.CompletedTask;
    }

    private async Task StartListeningAsync()
    {
        if (_listening || IsSimulating) return;
        Geolocation.LocationChanged += OnLocationChanged;
        _listening = true;
        if (!Geolocation.IsListeningForeground)
        {
            var req = new GeolocationListeningRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(1));
            await Geolocation.StartListeningForegroundAsync(req);
        }
    }

    private void StopListening()
    {
        if (!_listening) return;
        Geolocation.LocationChanged -= OnLocationChanged;
        if (Geolocation.IsListeningForeground)
            Geolocation.StopListeningForeground();
        _listening = false;
    }

    private void OnLocationChanged(object? sender, GeolocationLocationChangedEventArgs e)
    {
        var loc = e.Location;
        _engine.UpdateLocation(new LocationFix(loc.Latitude, loc.Longitude, loc.Accuracy, loc.Course, loc.Speed));
    }

    private static void OnMain(Action action)
    {
        if (MainThread.IsMainThread) action();
        else MainThread.BeginInvokeOnMainThread(action);
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
