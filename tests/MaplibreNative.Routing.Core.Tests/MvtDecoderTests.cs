using MaplibreNative.Routing.Core.Mvt;
using Xunit;
using static MaplibreNative.Routing.Core.Tests.TestTile;

namespace MaplibreNative.Routing.Core.Tests;

public class MvtDecoderTests
{
    private static readonly byte[] Tile = Build(("transportation_name",
    [
        new Line(Props(("name", "Elm Street"), ("class", "minor")), (100, 200), (300, 200)),
    ]));

    [Fact]
    public void ReadsALayersLinesAndProperties()
    {
        var layer = MvtDecoder.Decode(Tile).GetLayer("transportation_name");
        Assert.NotNull(layer);
        var feature = Assert.Single(layer!.Features);
        Assert.Equal(MvtGeomType.LineString, feature.Type);
        Assert.Equal([(100, 200), (300, 200)], feature.Geometry[0]);
        Assert.Equal("Elm Street", MvtDecoder.GetProperties(feature, layer)["name"]);
    }

    [Fact]
    public void ReadsATileStillGzipped()
    {
        // As a server sends a PMTiles archive's tiles, whether or not they were asked for so
        var layer = MvtDecoder.Decode(Gzip(Tile)).GetLayer("transportation_name");
        Assert.Equal("Elm Street", MvtDecoder.GetProperties(layer!.Features[0], layer)["name"]);
    }
}
