using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Navigation;
using MaplibreNative.Routing.Core.Routing;
using Xunit;

namespace MaplibreNative.Routing.Core.Tests;

public class NavigationTests
{
    private static DirectionsRoute Route(params (double Lon, double Lat)[] shape) => TestRoutes.L(shape);

    [Fact]
    public void TheTrackerSnapsToTheLineAndMovesThroughTheSteps()
    {
        var t = new RouteProgressTracker(Route());
        // Halfway along the first segment, 20 m north of it: on the line between its vertices, not at one
        var p = t.Update(42.35 + 20 / 110574.0, -71.855);
        Assert.Equal(0, p.CurrentStepIndex);
        Assert.InRange(p.DistanceFromRouteMeters, 19, 21);
        Assert.InRange(p.SnappedLocation.Lon, -71.8551, -71.8549);
        Assert.InRange(p.StepDistanceRemainingMeters, 1220, 1250);   // about 1.5 segments of 820 m

        p = t.Update(42.3525, -71.84);   // after the turn
        Assert.Equal(1, p.CurrentStepIndex);
        Assert.False(p.HasArrived);
        p = t.Update(42.3598, -71.84);   // about 22 m short of the end
        Assert.True(p.HasArrived);
        Assert.Equal(1, p.CurrentStepIndex);
    }

    [Fact]
    public void LeavingTheRouteIsNoticedButNotATurnTakenEarly()
    {
        var route = Route();
        var tracker = new RouteProgressTracker(route);
        var off = new OffRouteDetector();
        bool Check(double lat, double lon, double? accuracy = null)
        {
            var fix = new LocationFix(lat, lon, accuracy);
            return off.IsOffRoute(fix, tracker.Update(fix));
        }

        Assert.False(Check(42.35, -71.86));              // the start: remembered, never off
        Assert.False(Check(42.35, -71.855));             // on the route
        Assert.False(Check(42.3503, -71.85));            // 33 m off: inside the 50 m radius
        Assert.True(Check(42.3510, -71.848));            // 110 m off
        // Off again only after moving 50 m on from there
        Assert.False(Check(42.3511, -71.848));
        Assert.True(Check(42.3520, -71.846));

        // A turn taken 30 m early is the next step, not off the route
        var early = new OffRouteDetector();
        var t2 = new RouteProgressTracker(route);
        early.IsOffRoute(new LocationFix(42.35, -71.86), t2.Update(42.35, -71.86));
        var fix = new LocationFix(42.3505, -71.8404);
        Assert.False(early.IsOffRoute(fix, t2.Update(fix)));

        // A poor fix widens the radius: 110 m off with 150 m accuracy isn't off
        var vague = new OffRouteDetector();
        var t3 = new RouteProgressTracker(route);
        vague.IsOffRoute(new LocationFix(42.35, -71.86), t3.Update(42.35, -71.86));
        fix = new LocationFix(42.3510, -71.848, 150);
        Assert.False(vague.IsOffRoute(fix, t3.Update(fix)));
    }

    [Fact]
    public void GoingTheWrongWayIsLeavingTheRoute()
    {
        var tracker = new RouteProgressTracker(Route());
        var off = new OffRouteDetector();
        var start = new LocationFix(42.35, -71.845);
        off.IsOffRoute(start, tracker.Update(start));
        // Back along the route, west, on it the whole time: each fix further from the turn
        bool seen = false;
        foreach (double lon in new[] { -71.846, -71.847, -71.848, -71.849, -71.850 })
        {
            var fix = new LocationFix(42.35, lon);
            var p = tracker.Update(fix);
            Assert.True(p.DistanceFromRouteMeters < 1);
            seen |= off.IsOffRoute(fix, p);
        }
        Assert.True(seen);
    }

    /// <summary>A router for the tests: a straight line from wherever it's asked from to the destination.</summary>
    private sealed class StraightRouter : IRoutingEngine
    {
        public int Calls;
        public Task<DirectionsRoute?> RouteAsync(RouteOptions o)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult<DirectionsRoute?>(Calls == 1 ? Route() :
                Route((o.Origin.Lon, o.Origin.Lat), (o.Destination.Lon, o.Destination.Lat)));
        }
    }

    [Fact]
    public async Task MissingTheTurnPlansANewRouteFromThere()
    {
        var router = new StraightRouter();
        var engine = new NavigationEngine(new NavigationOptions { MinimumTimeBetweenReroutes = TimeSpan.Zero },
            _ => router);
        var options = new RouteOptions { Origin = (42.35, -71.86), Destination = (42.36, -71.84), Profile = RouteProfile.OfflineAuto };
        await engine.PlanAsync(options);
        var rerouted = new TaskCompletionSource<DirectionsRoute>();
        int offRoute = 0;
        engine.OffRoute += (_, _) => offRoute++;
        bool reroutingWhenTold = true;
        engine.Rerouted += (_, r) => { reroutingWhenTold = engine.IsRerouting; rerouted.TrySetResult(r); };

        // Straight on east past the turn, 600 m beyond it
        var sim = new RouteSimulator([(-71.86, 42.35), (-71.833, 42.35)], speedMps: 15);
        foreach (var fix in sim.All())
        {
            engine.UpdateLocation(fix);
            if (rerouted.Task.IsCompleted) break;
        }
        var route = await rerouted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, offRoute);
        Assert.Equal(2, router.Calls);
        Assert.Same(route, engine.Route);
        Assert.Equal(42.36, route.Legs[0].Shape[^1].Lat, 6);               // still to the destination
        Assert.True(route.Legs[0].Shape[0].Lon > -71.84);                 // from past the turn
        Assert.False(reroutingWhenTold);                                   // done when it says so (raced on CI)
        Assert.False(engine.IsRerouting);
    }

    [Fact]
    public async Task DrivingTheRouteArrivesWithoutRerouting()
    {
        var router = new StraightRouter();
        var engine = new NavigationEngine(engineFor: _ => router);
        await engine.PlanAsync(new RouteOptions { Origin = (42.35, -71.86), Destination = (42.36, -71.84), Profile = RouteProfile.OfflineAuto });
        int arrived = 0, off = 0, steps = 0, last = -1;
        engine.Arrived += (_, _) => arrived++;
        engine.OffRoute += (_, _) => off++;
        engine.ProgressUpdated += (_, p) => { if (p.CurrentStepIndex != last) { steps++; last = p.CurrentStepIndex; } };
        var sim = RouteSimulator.ForRoute(engine.Route!);
        sim.JitterMeters = 8;   // a real GPS wanders
        foreach (var fix in sim.All()) engine.UpdateLocation(fix);
        Assert.Equal((1, 0, 2), (arrived, off, steps));
        Assert.True(engine.HasArrived);
        Assert.Equal(1, router.Calls);
    }
}
