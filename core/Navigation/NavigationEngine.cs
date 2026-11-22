using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Routing;

namespace MaplibreNative.Routing.Core.Navigation;

/// <summary>The user has left the route: where, and how far along it they had come.</summary>
public sealed class OffRouteEventArgs(LocationFix location, RouteProgress progress) : EventArgs
{
    public LocationFix Location { get; } = location;
    public RouteProgress Progress { get; } = progress;
}

public sealed class RerouteFailedEventArgs(LocationFix from, string reason, Exception? exception = null) : EventArgs
{
    public LocationFix From { get; } = from;
    public string Reason { get; } = reason;
    public Exception? Exception { get; } = exception;
}

/// <summary>
/// Navigation along a route, the same on every platform: plans the route, follows the user along it from each
/// location fix (<see cref="UpdateLocation"/>), announces the turns, notices when the user leaves the route and,
/// with <see cref="NavigationOptions.AutoReroute"/>, plans a new one from where they are. A platform's session feeds
/// it fixes, from the GPS or <see cref="RouteSimulator"/>, and shows what it raises.
///
/// Events are raised on the thread that called <see cref="UpdateLocation"/>, except <see cref="Rerouted"/> and
/// <see cref="RerouteFailed"/>, which come from the reroute's own task. A UI marshals them.
/// </summary>
public sealed class NavigationEngine
{
    private readonly object _gate = new();
    private readonly Func<RouteProfile, IRoutingEngine> _engineFor;
    private readonly OffRouteDetector _detector;
    private RouteProgressTracker? _tracker;
    private ManeuverAnnouncerHelper? _announcer;
    private List<DirectionsRoute> _alternatives = [];
    private DateTime _lastRerouteAt = DateTime.MinValue;
    private bool _arrived;
    private int _rerouting;

    public NavigationEngine(NavigationOptions? options = null, Func<RouteProfile, IRoutingEngine>? engineFor = null)
    {
        Options = options ?? new NavigationOptions();
        _engineFor = engineFor ?? RoutingEngines.For;
        _detector = new OffRouteDetector(Options);
    }

    public NavigationOptions Options { get; }

    /// <summary>What the route was planned with; a reroute uses the same, from where the user is.</summary>
    public RouteOptions? RouteOptions { get; private set; }

    public DirectionsRoute? Route { get; private set; }
    public IReadOnlyList<DirectionsRoute> Alternatives => _alternatives;
    public int SelectedIndex { get; private set; }
    public RouteProgress? Progress { get; private set; }
    public bool IsRerouting => Volatile.Read(ref _rerouting) == 1;
    public bool HasArrived => _arrived;

    public event EventHandler<RouteProgress>? ProgressUpdated;
    public event EventHandler<ManeuverAnnouncementEventArgs>? AnnouncementNeeded;
    /// <summary>The user has left the route (and, with AutoReroute, a new one is being planned).</summary>
    public event EventHandler<OffRouteEventArgs>? OffRoute;
    public event EventHandler<DirectionsRoute>? Rerouted;
    public event EventHandler<RerouteFailedEventArgs>? RerouteFailed;
    public event EventHandler<RouteProgress>? Arrived;

    /// <summary>Plans up to <paramref name="maxRoutes"/> routes, shortest first, and follows the first.</summary>
    public async Task<IReadOnlyList<DirectionsRoute>> PlanAsync(RouteOptions options, int maxRoutes = 1)
    {
        var routes = await _engineFor(options.Profile).FindAlternativesAsync(options, maxRoutes).ConfigureAwait(false);
        lock (_gate)
        {
            RouteOptions = options;
            _alternatives = [.. routes];
            if (_alternatives.Count > 0) Follow(_alternatives[0], 0);
            else Route = null;
        }
        return _alternatives;
    }

    /// <summary>Follows a route planned elsewhere; a reroute plans with <paramref name="options"/>.</summary>
    public void Start(DirectionsRoute route, RouteOptions? options = null)
    {
        lock (_gate)
        {
            RouteOptions = options ?? RouteOptions;
            _alternatives = [route];
            Follow(route, 0);
        }
    }

    /// <summary>Follows another of the routes planned.</summary>
    public void SelectAlternative(int index)
    {
        lock (_gate)
        {
            if (index < 0 || index >= _alternatives.Count) return;
            Follow(_alternatives[index], index);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            Route = null;
            Progress = null;
            _tracker = null;
            _announcer = null;
            _alternatives = [];
        }
    }

    private void Follow(DirectionsRoute route, int index)
    {
        Route = route;
        SelectedIndex = index;
        Progress = null;
        _arrived = false;
        _tracker = new RouteProgressTracker(route);
        _announcer = new ManeuverAnnouncerHelper();
        _announcer.AnnouncementNeeded += (s, e) => AnnouncementNeeded?.Invoke(this, e);
        _detector.Reset();
    }

    /// <summary>One location fix: where the user is on the route, and whether they've left it.</summary>
    public RouteProgress? UpdateLocation(LocationFix fix)
    {
        RouteProgress progress;
        bool arrivedNow, offRoute;
        ManeuverAnnouncerHelper? announcer;
        lock (_gate)
        {
            if (_tracker is null || Route is null) return null;
            progress = _tracker.Update(fix);
            Progress = progress;
            announcer = _announcer;
            arrivedNow = progress.HasArrived && !_arrived;
            if (arrivedNow) _arrived = true;
            // While a new route is being found, the old one is still followed, but not left again
            offRoute = !_arrived && !IsRerouting && _detector.IsOffRoute(fix, progress);
        }

        ProgressUpdated?.Invoke(this, progress);
        announcer?.Update(progress);
        if (arrivedNow) Arrived?.Invoke(this, progress);
        if (offRoute)
        {
            OffRoute?.Invoke(this, new OffRouteEventArgs(fix, progress));
            if (Options.AutoReroute) _ = RerouteAsync(fix);
        }
        return progress;
    }

    /// <summary>
    /// Plans a new route from <paramref name="from"/> to the destination, with the options the route was planned
    /// with, and follows it. One at a time, and not more often than
    /// <see cref="NavigationOptions.MinimumTimeBetweenReroutes"/>: null when it doesn't run, or finds nothing.
    /// </summary>
    public async Task<DirectionsRoute?> RerouteAsync(LocationFix from)
    {
        var options = RouteOptions;
        if (options is null) return null;
        if (DateTime.UtcNow - _lastRerouteAt < Options.MinimumTimeBetweenReroutes) return null;
        if (Interlocked.Exchange(ref _rerouting, 1) == 1) return null;
        _lastRerouteAt = DateTime.UtcNow;
        try
        {
            using var timeout = new CancellationTokenSource(Options.RerouteTimeout);
            var again = options with { Origin = (from.Lat, from.Lon), CancellationToken = timeout.Token, Progress = null };
            var route = await Task.Run(() => _engineFor(again.Profile).RouteAsync(again)).ConfigureAwait(false);
            if (route is null)
            {
                RerouteFailed?.Invoke(this, new RerouteFailedEventArgs(from, "No route from here"));
                return null;
            }
            lock (_gate)
            {
                if (Route is null) return null;   // stopped meanwhile
                _alternatives = [route];
                Follow(route, 0);
                _detector.Reset((from.Lat, from.Lon));
            }
            Rerouted?.Invoke(this, route);
            return route;
        }
        catch (Exception ex)
        {
            RerouteFailed?.Invoke(this, new RerouteFailedEventArgs(from,
                ex is OperationCanceledException ? "Rerouting took too long" : ex.Message, ex));
            return null;
        }
        finally
        {
            Volatile.Write(ref _rerouting, 0);
        }
    }
}
