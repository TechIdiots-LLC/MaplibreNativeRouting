using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Navigation;
using Xunit;

namespace MaplibreNative.Routing.Core.Tests;

public class RouteSimulatorTests
{
    private static DirectionsRoute Route() => TestRoutes.L();

    [Fact]
    public void TheSimulatorDrivesTheRouteAtItsStepsSpeeds()
    {
        var sim = RouteSimulator.ForRoute(Route());
        var fixes = sim.All().ToList();
        Assert.Equal((42.35, -71.86), (fixes[0].Lat, fixes[0].Lon));
        Assert.Equal(90, fixes[0].Bearing!.Value, 0);                     // east
        Assert.Equal(1640 / 120.0, fixes[1].SpeedMps!.Value, 3);          // the first step's speed
        Assert.Equal(0, fixes[^1].Bearing!.Value, 0);                     // north at the end
        Assert.Equal(42.36, fixes[^1].Lat, 6);
        // About as many seconds as the route was timed at
        Assert.InRange(fixes.Count, 280 - 10, 280 + 10);
        Assert.Null(sim.Next());

        var fixed10 = new RouteSimulator([(-71.86, 42.35), (-71.85, 42.35)], speedMps: 10);
        var two = fixed10.All().Take(2).ToList();
        Assert.InRange(Distance(two[0], two[1]), 9.9, 10.1);
    }

    private static double Distance(LocationFix a, LocationFix b) =>
        Utils.RouteUtils.HaversineMeters(a.Lat, a.Lon, b.Lat, b.Lon);

    [Fact]
    public async Task TheSimulatorRunsInRealTimeFasterWhenAsked()
    {
        var sim = new RouteSimulator([(-71.86, 42.35), (-71.85, 42.35)], speedMps: 200) { Interval = TimeSpan.FromSeconds(1), SpeedFactor = 50 };
        var fixes = new List<LocationFix>();
        var done = new TaskCompletionSource();
        sim.LocationChanged += (_, f) => { lock (fixes) fixes.Add(f); };
        sim.Finished += (_, _) => done.TrySetResult();
        sim.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));   // 820 m at 200 m/s: 5 s of driving, 0.1 s of real time
        Assert.InRange(fixes.Count, 5, 7);
        Assert.False(sim.IsRunning);
    }
}
