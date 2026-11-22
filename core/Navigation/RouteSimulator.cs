using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Navigation;

/// <summary>
/// Drives a line as location fixes, to try navigation without going anywhere: a route at the speeds its steps were
/// timed at, or any path (one that leaves the route tries rerouting). Each fix is <see cref="Interval"/> of driving
/// further on, with its bearing. Run it with <see cref="Start"/> (a fix every <see cref="Interval"/> of real time,
/// divided by <see cref="SpeedFactor"/>), or step it with <see cref="Next"/>.
///
/// Ported from maplibre-navigation-android's ReplayRouteLocationEngine / ReplayRouteLocationConverter (NOTICE.md),
/// which slice a route into points every speed × interval metres.
/// </summary>
public sealed class RouteSimulator : IDisposable
{
    /// <summary>maplibre-navigation-android's replay speed, 45 km/h, for a path with no timing.</summary>
    public const double DefaultSpeedMps = 45 / 3.6;

    private readonly IReadOnlyList<(double Lon, double Lat)> _path;
    private readonly double[] _cumulative;
    private readonly double[] _speedBySegment;
    private readonly Random _noise = new(1);
    private double _along;
    private CancellationTokenSource? _run;

    /// <param name="path">The line to drive, [lon, lat].</param>
    /// <param name="speedMps">How fast; or <paramref name="speedBySegment"/>.</param>
    /// <param name="speedBySegment">How fast along each of the path's segments, metres a second.</param>
    public RouteSimulator(IReadOnlyList<(double Lon, double Lat)> path, double speedMps = DefaultSpeedMps,
        IReadOnlyList<double>? speedBySegment = null)
    {
        _path = path;
        _cumulative = RouteGeometry.Cumulative(path);
        _speedBySegment = new double[Math.Max(0, path.Count - 1)];
        for (int i = 0; i < _speedBySegment.Length; i++)
            _speedBySegment[i] = speedBySegment is not null && i < speedBySegment.Count && speedBySegment[i] > 0
                ? speedBySegment[i] : speedMps;
    }

    /// <summary>
    /// A route, driven at the speed each step was timed at (its distance over its duration), or at
    /// <paramref name="speedMps"/> throughout.
    /// </summary>
    public static RouteSimulator ForRoute(DirectionsRoute route, double? speedMps = null)
    {
        var path = new List<(double Lon, double Lat)>();
        var speeds = new List<double>();   // speeds[i]: from path[i] to path[i + 1]
        foreach (var leg in route.Legs)
        {
            for (int i = 0; i < leg.Shape.Count; i++)
            {
                if (i == 0 && path.Count > 0)
                {
                    if (path[^1] == leg.Shape[0]) continue;   // a leg starts where the last ended
                    speeds.Add(speedMps ?? DefaultSpeedMps);
                }
                else if (i > 0)
                {
                    var step = leg.Steps.LastOrDefault(st => st.BeginShapeIndex <= i - 1 && st.Distance > 0);
                    speeds.Add(speedMps ?? (step is { Duration: > 0 } ? step.Distance / step.Duration : DefaultSpeedMps));
                }
                path.Add(leg.Shape[i]);
            }
        }
        return new RouteSimulator(path, speedMps ?? DefaultSpeedMps, speeds);
    }

    /// <summary>Time between fixes, in simulated time.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How much faster than real time to drive: 2 drives a minute's route in 30 seconds.</summary>
    public double SpeedFactor { get; set; } = 1;

    /// <summary>The accuracy each fix claims.</summary>
    public double AccuracyMeters { get; set; } = 5;

    /// <summary>Random scatter added to each fix, metres, as a real GPS has (0 for none).</summary>
    public double JitterMeters { get; set; }

    public double LengthMeters => _cumulative.Length > 0 ? _cumulative[^1] : 0;
    public double DistanceDrivenMeters => _along;
    public bool IsFinished => _along >= LengthMeters;
    public bool IsRunning => _run is not null;

    /// <summary>Each fix, as <see cref="Start"/> drives.</summary>
    public event EventHandler<LocationFix>? LocationChanged;

    /// <summary>The end of the path is reached.</summary>
    public event EventHandler? Finished;

    /// <summary>The fix where the drive is now, without moving.</summary>
    public LocationFix Current() => FixAt(_along, SpeedAt(_along));

    /// <summary>
    /// The next fix: the start itself first, then one <see cref="Interval"/> of driving further each time; null once
    /// the end has been reported.
    /// </summary>
    public LocationFix? Next()
    {
        if (_path.Count == 0) return null;
        if (!_reportedStart)
        {
            _reportedStart = true;
            return Current();
        }
        if (IsFinished) return null;
        double speed = SpeedAt(_along);
        _along = Math.Min(LengthMeters, _along + speed * Interval.TotalSeconds);
        return FixAt(_along, speed);
    }

    private bool _reportedStart;

    /// <summary>Every fix to the end, for a test.</summary>
    public IEnumerable<LocationFix> All()
    {
        while (Next() is { } fix) yield return fix;
    }

    /// <summary>Moves to a distance along the path, metres.</summary>
    public void Seek(double alongMeters)
    {
        _along = Math.Clamp(alongMeters, 0, LengthMeters);
        _reportedStart = false;
    }

    /// <summary>Drives in real time: a fix every <see cref="Interval"/> divided by <see cref="SpeedFactor"/>.</summary>
    public void Start()
    {
        if (_run is not null) return;
        var run = _run = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                while (!run.IsCancellationRequested)
                {
                    if (Next() is not { } fix)
                    {
                        Finished?.Invoke(this, EventArgs.Empty);
                        break;
                    }
                    LocationChanged?.Invoke(this, fix);
                    await Task.Delay(TimeSpan.FromSeconds(Interval.TotalSeconds / Math.Max(0.01, SpeedFactor)), run.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (ReferenceEquals(_run, run)) _run = null;
                run.Dispose();
            }
        });
    }

    public void Stop()
    {
        var run = _run;
        _run = null;
        try { run?.Cancel(); } catch (ObjectDisposedException) { }
    }

    public void Dispose() => Stop();

    private double SpeedAt(double along)
    {
        if (_speedBySegment.Length == 0) return DefaultSpeedMps;
        int i = Array.BinarySearch(_cumulative, along);
        if (i < 0) i = ~i - 1;
        return _speedBySegment[Math.Clamp(i, 0, _speedBySegment.Length - 1)];
    }

    private LocationFix FixAt(double along, double speed)
    {
        var (lat, lon, bearing) = RouteGeometry.PointAlong(_path, _cumulative, along);
        if (JitterMeters > 0)
        {
            double angle = _noise.NextDouble() * 2 * Math.PI, r = _noise.NextDouble() * JitterMeters;
            lat += r * Math.Cos(angle) / 110_574;
            lon += r * Math.Sin(angle) / (111_320 * Math.Cos(lat * Math.PI / 180));
        }
        return new LocationFix(lat, lon, AccuracyMeters, bearing, speed);
    }
}
