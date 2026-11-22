using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Navigation;
using MaplibreNative.Routing.Core.Routing;
using Xunit;

namespace MaplibreNative.Routing.Core.Tests;

public class WaypointTests
{
    /// <summary>A straight line between whatever it's asked for, remembering each request.</summary>
    private sealed class StraightRouter : IRoutingEngine
    {
        public readonly List<RouteOptions> Asked = [];
        public Task<DirectionsRoute?> RouteAsync(RouteOptions o)
        {
            lock (Asked) Asked.Add(o);
            var route = TestRoutes.L((o.Origin.Lon, o.Origin.Lat), (o.Destination.Lon, o.Destination.Lat));
            double meters = Utils.RouteUtils.HaversineMeters(o.Origin.Lat, o.Origin.Lon, o.Destination.Lat, o.Destination.Lon);
            var leg = route.Legs[0];
            var steps = leg.Steps.ToList();
            steps[0] = new LegStep { Type = ManeuverType.Start, BeginShapeIndex = 0, EndShapeIndex = 1, Distance = meters, Duration = meters / 10, Instruction = "Head on" };
            return Task.FromResult<DirectionsRoute?>(route with
            {
                Distance = meters, Duration = meters / 10,
                Legs = [new RouteLeg { Shape = leg.Shape, Steps = steps, Distance = meters, Duration = meters / 10 }],
            });
        }
    }

    private static readonly (double Lat, double Lon) A = (42.35, -71.86), S1 = (42.35, -71.85), S2 = (42.35, -71.84), B = (42.36, -71.84);

    [Fact]
    public void StopsGoWhereTheyLengthenTheTripLeast()
    {
        var w = new RouteWaypoints();
        int changes = 0;
        w.Changed += (_, _) => changes++;
        w.SetOrigin(A);
        w.SetDestination(B);
        Assert.Equal(1, w.AddStop(S2));            // between A and B
        Assert.Equal(1, w.AddStop(S1));            // between A and S2, not S2 and B
        Assert.Equal([A, S1, S2, B], w.Points);
        Assert.Equal([WaypointKind.Origin, WaypointKind.Stop, WaypointKind.Stop, WaypointKind.Destination],
            Enumerable.Range(0, 4).Select(w.KindOf));
        Assert.Equal(1, w.NearestPoint((42.3501, -71.8501), 50));
        Assert.Null(w.NearestPoint((42.352, -71.855), 50));

        var o = w.ApplyTo(new RouteOptions { Origin = default, Destination = default, Profile = RouteProfile.OfflineAuto });
        Assert.Equal((A, B), (o.Origin, o.Destination));
        Assert.Equal([S1, S2], o.Waypoints);
        w.RemoveAt(1);
        Assert.Equal([A, S2, B], w.Points);
        Assert.Equal(5, changes);
    }

    [Fact]
    public async Task APointOnTheRouteGoesBetweenTheStopsOfItsLeg()
    {
        var w = new RouteWaypoints();
        w.SetOrigin(A);
        w.SetDestination(B);
        w.AddStop(S2);
        var route = await MultiStopRouting.RouteAsync(new StraightRouter(), w.ApplyTo(new RouteOptions { Origin = default, Destination = default, Profile = RouteProfile.OfflineAuto }));
        Assert.Equal(2, route!.Legs.Count);
        // On the first leg (A to S2), 20 m off it
        Assert.Equal(1, w.InsertOnRoute((42.3502, -71.85), route, maxMeters: 50));
        Assert.Equal([A, (42.3502, -71.85), S2, B], w.Points);
        Assert.Null(w.InsertOnRoute((42.40, -71.85), route, maxMeters: 50));   // nowhere near it
    }

    [Fact]
    public async Task ARouteThroughStopsIsDrivenLegByLegAndEachStopSaid()
    {
        var router = new StraightRouter();
        var engine = new NavigationEngine(engineFor: _ => router);
        var routes = await engine.PlanAsync(new RouteOptions { Origin = A, Destination = B, Waypoints = [S1, S2], Profile = RouteProfile.OfflineAuto }, maxRoutes: 3);
        var route = Assert.Single(routes);                        // no alternatives through stops
        Assert.Equal(3, route.Legs.Count);
        Assert.Equal(route.Legs.Sum(l => l.Distance), route.Distance, 6);
        Assert.Equal([(A, S1), (S1, S2), (S2, B)], router.Asked.Select(o => (o.Origin, o.Destination)));

        var reached = new List<int>();
        var said = new List<string>();
        engine.WaypointReached += (_, i) => reached.Add(i);
        engine.VoiceInstruction += (_, t) => said.Add(t);
        foreach (var fix in RouteSimulator.ForRoute(route).All()) engine.UpdateLocation(fix);
        Assert.Equal([0, 1], reached);
        Assert.Contains("You have reached stop 1", said);
        Assert.Contains("You have reached stop 2", said);
        Assert.True(engine.HasArrived);
    }

    [Fact]
    public async Task AReroutePastAStopKeepsOnlyTheStopsAhead()
    {
        var router = new StraightRouter();
        var engine = new NavigationEngine(new NavigationOptions { MinimumTimeBetweenReroutes = TimeSpan.Zero }, _ => router);
        await engine.PlanAsync(new RouteOptions { Origin = A, Destination = B, Waypoints = [S1, S2], Profile = RouteProfile.OfflineAuto });
        // Drive the first leg to S1, on into the second, then off the route
        foreach (var fix in new RouteSimulator([(A.Lon, A.Lat), (S1.Lon, S1.Lat), (-71.847, 42.35)], 10).All()) engine.UpdateLocation(fix);
        Assert.Equal(1, engine.Progress!.CurrentLegIndex);
        router.Asked.Clear();
        var rerouted = await engine.RerouteAsync(new LocationFix(42.352, -71.847));
        Assert.NotNull(rerouted);
        Assert.Equal([((42.352, -71.847), S2), (S2, B)], router.Asked.Select(o => (o.Origin, o.Destination)));

        // S2, reached on the new route, is still stop 2
        var reached = new List<int>();
        engine.WaypointReached += (_, i) => reached.Add(i);
        foreach (var fix in RouteSimulator.ForRoute(rerouted!).All()) engine.UpdateLocation(fix);
        Assert.Equal([1], reached);
    }
}
