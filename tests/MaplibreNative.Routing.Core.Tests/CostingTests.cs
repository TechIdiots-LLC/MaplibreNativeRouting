using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Mvt;
using MaplibreNative.Routing.Core.Routing;
using Xunit;
using static MaplibreNative.Routing.Core.Tests.TestTile;

namespace MaplibreNative.Routing.Core.Tests;

public class CostingTests
{
    private static readonly TileCoord Here = TileCoord.FromLatLon(44.20, -71.24, 14);

    private static (double Lat, double Lon) P(int x, int y) => Here.FeatureToLatLon(x, y, 4096);

    /// <summary>A trunk road (a two-lane highway, as NH 16 is) with a parking lot's loop beside it, a little longer.</summary>
    private sealed class TrunkWithParking : ITileCacheProvider
    {
        private static readonly byte[] Tile = Build(("transportation",
        [
            new Line(Props(("class", "trunk")), (200, 2000), (3800, 2000)),
            new Line(Props(("class", "service")), (1500, 2000), (1550, 1960), (2450, 1960), (2500, 2000)),
        ]), ("transportation_name",
        [
            new Line(Props(("name", "Pinkham Notch Road"), ("ref", "16"), ("network", "us-state"), ("class", "trunk")), (200, 2000), (3800, 2000)),
        ]));

        public Task<byte[]?> GetTileAsync(TileCoord coord, CancellationToken ct = default) =>
            Task.FromResult<byte[]?>(coord == Here ? Tile : []);
        public Task SetTileAsync(TileCoord coord, byte[] data, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequestAreaCacheAsync(double minLat, double minLon, double maxLat, double maxLon, int zoom,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    private static RouteOptions Trip(RouteProfile profile) => new()
    {
        Origin = P(300, 2000), Destination = P(3700, 2000), Profile = profile,
        MvtTileJsonUrl = "https://tiles.invalid/costing/{z}/{x}/{y}.pbf", TileCacheProvider = new TrunkWithParking(),
    };

    [Theory]
    [InlineData(RouteProfile.HybridOfflineMotorcycle)]
    [InlineData(RouteProfile.OfflineAuto)]
    public async Task AParkingLotBesideTheRoadIsNoShortcut(RouteProfile profile)
    {
        var route = await RoutingEngines.For(profile).RouteAsync(Trip(profile));
        Assert.NotNull(route);
        Assert.DoesNotContain(route!.Legs[0].Steps, s => s.RoadClass == "service");
    }

    [Fact]
    public async Task ARoutesTimeIsItsDrivingTimeNotItsCost()
    {
        // 90 km/h on a trunk for a car, however much the highway preference weighs it
        var route = await RoutingEngines.For(RouteProfile.OfflineAuto).RouteAsync(Trip(RouteProfile.OfflineAuto));
        Assert.NotNull(route);
        Assert.InRange(route!.Duration, route.Distance / (90 / 3.6) * 0.95, route.Distance / (90 / 3.6) * 1.05);
        Assert.InRange(route.Legs[0].Steps.Sum(s => s.Duration), route.Duration * 0.95, route.Duration * 1.05);
    }

    /// <summary>Main Street bends left at a junction where Side Road carries straight on.</summary>
    private sealed class Bend : ITileCacheProvider
    {
        private static readonly byte[] Tile = Build(("transportation",
        [
            new Line(Props(("class", "minor")), (500, 3000), (2000, 3000), (2000, 1000)),
            new Line(Props(("class", "minor")), (2000, 3000), (3500, 3000)),
        ]), ("transportation_name",
        [
            new Line(Props(("name", "Main Street"), ("class", "minor")), (500, 3000), (2000, 3000), (2000, 1000)),
            new Line(Props(("name", "Side Road"), ("class", "minor")), (2000, 3000), (3500, 3000)),
        ]));

        public Task<byte[]?> GetTileAsync(TileCoord coord, CancellationToken ct = default) =>
            Task.FromResult<byte[]?>(coord == Here ? Tile : []);
        public Task SetTileAsync(TileCoord coord, byte[] data, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequestAreaCacheAsync(double minLat, double minLon, double maxLat, double maxLon, int zoom,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task ATurnThatKeepsToTheRoadSaysSo()
    {
        var route = await RoutingEngines.For(RouteProfile.OfflineAuto).RouteAsync(new RouteOptions
        {
            Origin = P(600, 3000), Destination = P(2000, 1100), Profile = RouteProfile.OfflineAuto,
            MvtTileJsonUrl = "https://tiles.invalid/bend/{z}/{x}/{y}.pbf", TileCacheProvider = new Bend(),
        });
        Assert.NotNull(route);
        var steps = route!.Legs[0].Steps;
        Assert.Contains(steps, s => s.Instruction == "Turn left to stay on Main Street");
        Assert.Contains(steps, s => s.VerbalPreInstruction?.EndsWith("turn left to stay on Main Street") == true);
    }
}
