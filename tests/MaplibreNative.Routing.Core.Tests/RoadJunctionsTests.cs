using MaplibreNative.Routing.Core.Graph;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Routing;
using Xunit;

namespace MaplibreNative.Routing.Core.Tests;

public class RoadJunctionsTests
{
    private static MvtRoadSegment Road(string name, string? brunnel = null, params (double Lon, double Lat)[] line) => new()
    {
        Coordinates = line, RoadClass = "minor", Name = name, Brunnel = brunnel,
    };

    /// <summary>How many separate pieces a graph of these roads is in.</summary>
    private static int Pieces(IReadOnlyList<MvtRoadSegment> roads)
    {
        var g = RoadGraph.Build(roads, new MvtCostingModel(RouteProfile.OfflineAuto, 0.1, 0));
        var seen = new HashSet<int>();
        int pieces = 0;
        foreach (var start in g.Nodes)
        {
            if (!seen.Add(start.Id)) continue;
            pieces++;
            var todo = new Stack<int>([start.Id]);
            while (todo.Count > 0)
            {
                int v = todo.Pop();
                foreach (var e in g.ForwardAdjacency[v].Concat(g.BackwardAdjacency[v]))
                    foreach (int w in new[] { e.FromNodeId, e.ToNodeId })
                        if (seen.Add(w)) todo.Push(w);
            }
        }
        return pieces;
    }

    [Fact]
    public void TwoRoadsCrossingWithNoSharedVertexAreJoined()
    {
        // As a tile draws them: the straight road simplified to its two ends, so nothing marks the crossing
        var roads = new[]
        {
            Road("Main Street", null, (-71.860, 42.350), (-71.840, 42.350)),
            Road("Elm Street", null, (-71.850, 42.340), (-71.850, 42.360)),
        };
        Assert.Equal(2, Pieces(roads));
        Assert.Equal(1, Pieces(RoadJunctions.Connect(roads)));
    }

    [Fact]
    public void ABridgeOverARoadIsNotJoinedToIt()
    {
        var roads = new[]
        {
            Road("Main Street", null, (-71.860, 42.350), (-71.840, 42.350)),
            Road("Route 9", "bridge", (-71.850, 42.340), (-71.850, 42.360)),
        };
        Assert.Equal(2, Pieces(RoadJunctions.Connect(roads)));
    }

    [Fact]
    public void AnEndOnAnotherRoadIsPutOntoIt()
    {
        // A tile's edge: one piece stops a metre past where the next tile's piece of the same road starts,
        // and a side street ends a metre short of the road it meets
        var roads = new[]
        {
            Road("Main Street", null, (-71.860, 42.350), (-71.849990, 42.350)),
            Road("Main Street", null, (-71.850010, 42.350), (-71.840, 42.350)),
            Road("Oak Street", null, (-71.845, 42.340), (-71.845, 42.349991)),
        };
        Assert.Equal(3, Pieces(roads));
        Assert.Equal(1, Pieces(RoadJunctions.Connect(roads)));
    }

    [Fact]
    public void ATilesLinesAreClippedToTheTileItself()
    {
        // In from the margin, across, out past the right edge, back in, and a piece only in the margin
        var parts = Routing.MvtGraphBuilder.ClipToTile([(-100, 100), (200, 100), (200, 4200), (300, 4200), (300, 3000)], 4096);
        Assert.Equal(2, parts.Count);
        Assert.Equal([(0.0, 100.0), (200.0, 100.0), (200.0, 4096.0)], parts[0]);
        Assert.Equal([(300.0, 4096.0), (300.0, 3000.0)], parts[1]);
        Assert.Empty(Routing.MvtGraphBuilder.ClipToTile([(-50, 10), (-10, 20)], 4096));
        Assert.Single(Routing.MvtGraphBuilder.ClipToTile([(10, 10), (20, 20), (30, 10)], 4096));
    }
}
