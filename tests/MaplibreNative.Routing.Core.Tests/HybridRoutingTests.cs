using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Mvt;
using MaplibreNative.Routing.Core.Routing;
using MaplibreNative.Routing.Core.Utils;
using Xunit;
using static MaplibreNative.Routing.Core.Tests.TestTile;

namespace MaplibreNative.Routing.Core.Tests;

public class HybridRoutingTests
{
    private static readonly TileCoord Here = TileCoord.FromLatLon(42.35, -71.85, 14);

    private static (double Lon, double Lat) P(int x, int y)
    {
        var (lat, lon) = Here.FeatureToLatLon(x, y, 4096);
        return (lon, lat);
    }

    /// <summary>
    /// The roads, from the app's cache: Main Street along the bottom, a side road up each end, and a straight road
    /// across the top; the trail (not in the tiles: the user's) arcs a little above that road.
    /// </summary>
    private sealed class Roads : ITileCacheProvider
    {
        private static readonly byte[] Tile = Build(("transportation",
        [
            new Line(Props(("class", "minor")), (200, 3000), (3800, 3000)),
            new Line(Props(("class", "minor")), (200, 3000), (200, 1000)),
            new Line(Props(("class", "minor")), (3800, 3000), (3800, 1000)),
            new Line(Props(("class", "minor")), (200, 1000), (3800, 1000)),
        ]), ("transportation_name",
        [
            new Line(Props(("name", "Main Street"), ("class", "minor")), (200, 3000), (3800, 3000)),
            new Line(Props(("name", "West Road"), ("class", "minor")), (200, 3000), (200, 1000)),
            new Line(Props(("name", "East Road"), ("class", "minor")), (3800, 3000), (3800, 1000)),
            new Line(Props(("name", "Ridge Road"), ("class", "minor")), (200, 1000), (3800, 1000)),
        ]));

        public Task<byte[]?> GetTileAsync(TileCoord coord, CancellationToken ct = default) =>
            Task.FromResult<byte[]?>(coord == Here ? Tile : []);
        public Task SetTileAsync(TileCoord coord, byte[] data, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequestAreaCacheAsync(double minLat, double minLon, double maxLat, double maxLon, int zoom,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    // 2.4% longer than Ridge Road below it; its ends a few metres from the road's
    private static readonly TrackFeature Trail = new()
    {
        Name = "Ridge Trail", Coordinates = [P(210, 990), P(2000, 600), P(3790, 990)],
    };

    private static RouteOptions Trip((double Lon, double Lat) from, (double Lon, double Lat) to, params RouteClosure[] closures) => new()
    {
        Origin = (from.Lat, from.Lon), Destination = (to.Lat, to.Lon), Profile = RouteProfile.HybridOfflineMotorcycle,
        MvtTileJsonUrl = "https://tiles.invalid/{z}/{x}/{y}.pbf", TileCacheProvider = new Roads(),
        TrackFeatures = [Trail], Closures = closures,
    };

    private static double OnTrail(DirectionsRoute r) =>
        r.Legs.SelectMany(l => l.Steps).Where(s => s.RoadClass == "trail").Sum(s => s.Distance) / r.Distance;

    [Fact]
    public async Task TheTrailIsTakenOverAShorterRoad()
    {
        var route = await new HybridGraphRouter().RouteAsync(Trip(P(200, 1000), P(3800, 1000)));
        Assert.NotNull(route);
        Assert.True(OnTrail(route!) > 0.95, $"{OnTrail(route!):P0} on the trail");
        Assert.Contains(route!.Legs[0].Steps, s => s.StreetNames.Contains("Ridge Trail"));
    }

    [Fact]
    public async Task AClosedTrailIsGoneRoundByRoad()
    {
        var closure = new RouteClosure(P(2000, 600).Lat, P(2000, 600).Lon, 40);
        var route = await new HybridGraphRouter().RouteAsync(Trip(P(200, 1000), P(3800, 1000), closure));
        Assert.NotNull(route);
        Assert.True(OnTrail(route!) < 0.6, $"{OnTrail(route!):P0} on the trail");
        Assert.Contains(route!.Legs[0].Steps, s => s.StreetNames.Contains("Ridge Road"));
        var shape = route.Legs[0].Shape;
        Assert.DoesNotContain(shape.Zip(shape.Skip(1)), p => closure.Blocks(p.First, p.Second));
    }

    [Fact]
    public async Task AStartOffTheTrailsIsJoinedByRoad()
    {
        // From the bottom-left corner on Main Street: up West Road to the trail, along it
        var route = await new HybridGraphRouter().RouteAsync(Trip(P(200, 3000), P(3800, 1000)));
        Assert.NotNull(route);
        var names = route!.Legs[0].Steps.SelectMany(s => s.StreetNames).ToList();
        Assert.Contains("West Road", names);
        Assert.Contains("Ridge Trail", names);
        var (lon, lat) = route.Legs[0].Shape[0];
        Assert.True(RouteUtils.HaversineMeters(lat, lon, P(200, 3000).Lat, P(200, 3000).Lon) < 20, "starts at the origin");
    }

    [Fact]
    public void ATrailEndNearARoadIsJoinedToIt()
    {
        // 30 m short of the road: a GPS-drawn trail's end, joined; a road's would stay apart
        var road = new MvtRoadSegment { Coordinates = [(-71.86, 42.35), (-71.84, 42.35)], RoadClass = "minor" };
        var trail = new MvtRoadSegment { Coordinates = [(-71.85, 42.36), (-71.85, 42.35 + 30 / 110574.0)], RoadClass = MvtCostingModel.TrailClass };
        var joined = Graph.RoadJunctions.Connect([road, trail]);
        Assert.Equal(42.35, joined[1].Coordinates[^1].Lat, 7);
        Assert.Equal(3, joined[0].Coordinates.Count);   // the road gets the point
        var notTrail = new MvtRoadSegment { Coordinates = trail.Coordinates, RoadClass = "minor" };
        Assert.NotEqual(42.35, Graph.RoadJunctions.Connect([road, notTrail])[1].Coordinates[^1].Lat, 7);
    }

    [Fact]
    public void AClosureCutsOnlyItsOwnFewMetresOutOfALongSegment()
    {
        // A 1.6 km segment with a 40 m closure in its middle: two pieces, each ending at the closure's edge
        var closure = new RouteClosure(42.35, -71.85, 40);
        var pieces = RouteClosure.Cut([(-71.86, 42.35), (-71.84, 42.35)], [closure]);
        Assert.Equal(2, pieces.Count);
        double Gap((double Lon, double Lat) p) => RouteUtils.HaversineMeters(42.35, -71.85, p.Lat, p.Lon);
        Assert.InRange(Gap(pieces[0][^1]), 39.5, 40.5);
        Assert.InRange(Gap(pieces[1][0]), 39.5, 40.5);
        Assert.Single(RouteClosure.Cut([(-71.86, 42.35), (-71.84, 42.35)], [new RouteClosure(42.36, -71.85, 40)]));
        Assert.Empty(RouteClosure.Cut([(-71.8501, 42.35), (-71.8499, 42.35)], [closure]));   // all inside
    }

    [Fact]
    public async Task AClosureMetOnTheWayIsGoneRound()
    {
        var engine = new Navigation.NavigationEngine();
        await engine.PlanAsync(Trip(P(200, 1000), P(3800, 1000)));
        Assert.True(OnTrail(engine.Route!) > 0.95);
        // A little way along the trail, word comes that its middle is closed
        foreach (var fix in new Navigation.RouteSimulator(engine.Route!.Legs[0].Shape).All().Take(20)) engine.UpdateLocation(fix);
        var closure = new RouteClosure(P(2000, 600).Lat, P(2000, 600).Lon, 40);
        var rerouted = await engine.AddClosureAsync(closure);
        Assert.NotNull(rerouted);
        Assert.Same(rerouted, engine.Route);
        Assert.Contains(closure, engine.RouteOptions!.Closures);
        var shape = rerouted!.Legs[0].Shape;
        Assert.DoesNotContain(shape.Zip(shape.Skip(1)), p => closure.Blocks(p.First, p.Second));
        Assert.Contains(rerouted.Legs[0].Steps, s => s.StreetNames.Contains("Ridge Road"));
    }
}
