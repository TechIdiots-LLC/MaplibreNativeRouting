using MaplibreNative.Routing.Core.Mvt;
using MaplibreNative.Routing.Core.Routing;
using Xunit;
using static MaplibreNative.Routing.Core.Tests.TestTile;

namespace MaplibreNative.Routing.Core.Tests;

public class RoadSearchTests
{
    private const double Lat = 42.35, Lon = -71.85;
    private static readonly TileCoord Here = TileCoord.FromLatLon(Lat, Lon, 14);

    /// <summary>The town's tile, from the app's cache: no tile server asked.</summary>
    private sealed class Town : ITileCacheProvider
    {
        private static readonly byte[] Tile = Build(("transportation_name",
        [
            new Line(Props(("name", "Elm Street"), ("class", "minor")), (100, 2048), (3000, 2048)),
            // Crossing Elm Street with no vertex where they cross, as a tile draws two straight roads
            new Line(Props(("name", "Main Street"), ("class", "minor")), (1500, 100), (1500, 4000)),
            new Line(Props(("name", "Highland Street"), ("class", "secondary"), ("ref", "31"), ("network", "us-state"),
                ("route_1_network", "US:MA")), (3000, 100), (3000, 4000)),
            new Line(Props(("name", "Pleasant Street"), ("class", "minor")), (100, 3500), (2000, 3500)),
            new Line(Props(("name", "Elm Path"), ("class", "path")), (200, 200), (600, 600)),
        ]));

        public Task<byte[]?> GetTileAsync(TileCoord coord, CancellationToken ct = default) =>
            Task.FromResult<byte[]?>(coord == Here ? Tile : []);
        public Task SetTileAsync(TileCoord coord, byte[] data, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequestAreaCacheAsync(double minLat, double minLon, double maxLat, double maxLon, int zoom,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    private static Task<IReadOnlyList<RoadMatch>> Find(string q) =>
        new RoadSearch("https://tiles.invalid/{z}/{x}/{y}.pbf", new Town()).FindAsync(q, Lat, Lon, radiusKm: 1);

    [Fact]
    public async Task ARoadIsFoundByItsNameHoweverWritten()
    {
        var elm = Assert.Single(await Find("elm st."));
        Assert.Equal(("Elm Street", RoadMatchLevel.Exact, false), (elm.Name, elm.Level, elm.IsCorner));
        Assert.Equal(Here.FeatureToLatLon(0, 2048, 4096).Lat, elm.Location.Lat, 6);
        Assert.Equal(RoadMatchLevel.Base, (await Find("Pleasant"))[0].Level);
        var misheard = (await Find("Pheasant Street"))[0];
        Assert.Equal(("Pleasant Street", RoadMatchLevel.Fuzzy), (misheard.Name, misheard.Level));
        Assert.Empty(await Find("Elm Path"));   // a footpath isn't a road to go to
        Assert.Empty(await Find("Nowhere Lane"));
    }

    [Fact]
    public async Task ARoadIsFoundByItsNumber()
    {
        foreach (var q in new[] { "Route 31", "rt 31", "MA 31", "State Route 31" })
            Assert.Equal("MA 31 (Highland Street)", (await Find(q))[0].Name);
    }

    [Fact]
    public async Task TwoRoadsNamedAreTheCornerWhereTheyMeet()
    {
        var found = await Find("Main St and Elm St");
        var corner = found[0];
        Assert.True(corner.IsCorner);
        Assert.Equal("Main Street and Elm Street", corner.Name);
        var (lat, lon) = Here.FeatureToLatLon(1500, 2048, 4096);
        Assert.Equal(lat, corner.Location.Lat, 6);
        Assert.Equal(lon, corner.Location.Lon, 6);
        Assert.Equal(["Main Street", "Elm Street"], found.Skip(1).Select(m => m.Name).OrderByDescending(n => n));
        Assert.DoesNotContain(await Find("Pleasant St and Highland St"), m => m.IsCorner);   // they don't meet
    }
}
