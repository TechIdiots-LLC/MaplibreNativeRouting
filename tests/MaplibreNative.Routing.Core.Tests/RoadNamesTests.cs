using MaplibreNative.Routing.Core.Mvt;
using MaplibreNative.Routing.Core.Routing;
using MaplibreNative.Routing.Core.Utils;
using Xunit;
using static MaplibreNative.Routing.Core.Tests.TestTile;

namespace MaplibreNative.Routing.Core.Tests;

public class RoadNamesTests
{
    [Fact]
    public void NamesReadTheWayTheyreSaid()
    {
        Assert.Equal("412 elm street", RoadNames.Words("412 Elm St."));
        Assert.Equal("calle penasco", RoadNames.Words("Calle Peñasco"));
        Assert.Equal("elm", RoadNames.Base("north elm street"));
        Assert.Equal("ann", RoadNames.Base("ann street east"));
        Assert.Equal("north street", RoadNames.Base("north street"));
        Assert.Null(RoadNames.Base("route 9"));
        Assert.Equal("5th", RoadNames.Base("5th avenue"));
        Assert.Contains("route 9", RoadNames.RefNames("MA 9"));
        Assert.Contains("interstate 290", RoadNames.RefNames("I-290"));
        Assert.Equal(["elm street", "route 9"], RoadNames.AddressRoads("412 Elm St and Route 9"));
        Assert.Equal(1, RoadNames.Distance("pheasant", "pleasant", 2));
        Assert.Equal(3, RoadNames.Distance("elm", "oak street", 2));
    }

    [Theory]
    [InlineData("31", "us-state", "US:MA", "MA 31")]
    [InlineData("190", "us-interstate", "US:I", "I 190")]
    [InlineData("20", "us-highway", null, "US 20")]
    [InlineData("9", "us-state", null, "SR 9")]
    [InlineData("A1", null, null, "A1")]
    public void ARoadNumberIsWrittenAsItsNetworkHasIt(string @ref, string? network, string? route1, string expected)
    {
        var props = new Dictionary<string, object> { ["ref"] = @ref };
        if (network != null) props["network"] = network;
        if (route1 != null) props["route_1_network"] = route1;
        Assert.Equal(expected, RoadNames.FormatRef(props));
    }

    private static MvtGraphBuilder.NameIndex Index(params Line[] named) =>
        new(MvtDecoder.Decode(Build(("transportation_name", named))).GetLayer("transportation_name"),
            new TileCoord(14, 4922, 6060), toleranceMeters: 12);

    [Fact]
    public void APieceIsNamedByTheNamedLineItLiesAlong()
    {
        // One named line, merged over the whole street: the piece shares no end with it
        var names = Index(new Line(Props(("name", "Elm Street"), ("class", "minor")), (-50, 105), (3000, 105)));
        Assert.Equal(("Elm Street", (string?)null), names.Find([(0, 100), (1000, 100)], "minor", 4096));
        Assert.Null(names.Find([(0, 400), (1000, 400)], "minor", 4096));   // 130 m off: another road
    }

    [Fact]
    public void OfTwoAsNearTheSameClassNamesIt()
    {
        var names = Index(
            new Line(Props(("name", "Bridge Road"), ("class", "primary")), (0, 104), (2000, 104)),
            new Line(Props(("name", "Main Street"), ("class", "minor"), ("ref", "122A"), ("network", "us-state"),
                ("route_1_network", "US:MA")), (0, 101), (2000, 101)));
        Assert.Equal(("Main Street", "MA 122A"), names.Find([(500, 102), (900, 102)], "minor", 4096));
        Assert.Equal(("Bridge Road", null), names.Find([(500, 104), (900, 104)], "primary", 4096));
    }
}
